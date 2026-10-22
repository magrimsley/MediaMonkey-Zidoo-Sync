using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Native composition of exporter, authoritative file transactions and device protocol.
/// Alpha.60.18.6: verifies reviewed changes, journals power ownership and reconciles interrupted runs before further execution.</summary>
public sealed class NativeProductionSyncService
{
    public event Action<string>? Output;
    public bool LastRunVerifiedDevice {get;private set;}
    public int LastRunCompletedChanges {get;private set;}
    public bool LastRunRecovered {get;private set;}
    internal sealed record Work(string FileName, byte[]? Content, int Count, string? OldHash, bool IsCreate=false, bool ReimportExisting=false);
    internal static Dictionary<string,string> RecoverySources(string root, IReadOnlyDictionary<string,string> baseline,
        IEnumerable<(string FileName,string SHA256)> targets)
    {
        var sources=new Dictionary<string,string>(baseline,StringComparer.OrdinalIgnoreCase);
        foreach(var target in targets)
        {
            if(target.FileName!=Path.GetFileName(target.FileName))throw new InvalidDataException("Recovery target must be a playlist filename.");
            string path=Path.Combine(root,target.FileName);
            NativeInterruptedRecovery.NoLink(path);
            if(!File.Exists(path) || Hash(File.ReadAllBytes(path))!=target.SHA256)
                throw new InvalidDataException("Playlist changed before recovery backup: "+target.FileName);
            sources[target.FileName]=path;
        }
        return sources;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public async Task<bool> RecoverPendingAsync(AppConfig config,Func<string,bool> confirm,CancellationToken ct=default)
    {
        ConfigurationService.Validate(config);
        string root=Path.GetFullPath(config.NAS.PlaylistFolderUNC);
        string marker=Path.Combine(root,".mmzs-production-pending.json");
        if(!File.Exists(marker)){Output?.Invoke("No interrupted synchronization requires recovery.");return false;}
        if((File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked recovery marker refused.");
        using var pending=JsonDocument.Parse(await File.ReadAllTextAsync(marker,ct));
        string journal=Path.GetFullPath(pending.RootElement.GetProperty("Journal").GetString()!);
        string? run=Path.GetDirectoryName(journal);
        string reports=Path.GetFullPath(Environment.ExpandEnvironmentVariables(config.Application.ReportRoot)).TrimEnd(Path.DirectorySeparatorChar);
        if(run is null || !string.Equals(Path.GetDirectoryName(run),reports,StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(run).StartsWith("NativeSync-",StringComparison.Ordinal) ||
            !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(journal),"^Transaction-[0-9a-f]{32}$"))
            throw new InvalidDataException("Recovery does not belong to the configured report folder.");
        foreach(string path in new[]{run,journal})if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked recovery folder refused.");
        byte[] intent=await File.ReadAllBytesAsync(Path.Combine(journal,"intent.json"),ct);
        using var doc=JsonDocument.Parse(intent);
        if(doc.RootElement.GetProperty("OriginalDevice").ValueKind==JsonValueKind.Null)
            throw new InvalidDataException("This interrupted creation requires review of its retained transaction receipt before recovery.");
        var original=JsonSerializer.Deserialize<ZidooPlaylistRecord>(doc.RootElement.GetProperty("OriginalDevice").GetRawText())!;
        string originalHash=doc.RootElement.GetProperty("ExpectedOldSHA256").GetString()!;
        if(!confirm("Restore the previous "+original.Name+" playlist ("+original.Count+" tracks)?\n\nThe Zidoo must be on. This restores the retained original state; it does not resume synchronization."))
        {Output?.Invoke("Recovery cancelled; retained files were not changed.");return false;}
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false};
        using var http=new HttpClient(handler){BaseAddress=new UriBuilder("http",config.Zidoo.IPAddress,config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        var device=new NativeZidooPlaylistDevice(http,config);device.Output+=message=>Output?.Invoke(message);await device.VerifyIdentityAsync(ct);await device.PreflightDirectoriesAsync(ct);
        await new NativeProductionRecovery(device).RecoverOriginalAsync(root,journal,Hash(intent),originalHash,ct);
        Output?.Invoke("Original "+original.Name+" restored and verified. Other playlists unchanged. Recovery completed.");return true;
    }

    public sealed record ReviewedChange(string FileName,int ExpectedCount,string? OriginalSHA256,string? CandidateSHA256=null,string? ChangeType=null, int? OriginalVisibility=null, bool ReimportExisting=false);
    private static byte[] ContentBytes(IEnumerable<string> lines)=>FrozenM3uContent.Bytes(lines);

    public async Task<IReadOnlyList<ReviewedChange>> PrepareReviewAsync(AppConfig config,CancellationToken ct=default)
    {
        ConfigurationService.Validate(config);
        var validation=await new MediaMonkeyPlaylistExporter().ValidateAsync(config,ct);
        var plan=await new MediaMonkeyExportTransactionPlanner().PrepareAsync(config,ct);
        var prepared=validation.PreparedPlaylists.ToDictionary(x=>x.M3UFile,StringComparer.OrdinalIgnoreCase);
        var counts=validation.PlaylistMap.ToDictionary(x=>x.M3UFile,x=>x.TrackCount,StringComparer.OrdinalIgnoreCase);
        var review=new List<ReviewedChange>();
        foreach(var item in plan.Items.Where(x=>x.Action!=PlaylistTransactionAction.Unchanged))
        {
            string path=Path.Combine(config.NAS.PlaylistFolderUNC,item.FileName);
            string? oldHash=File.Exists(path)?Hash(await File.ReadAllBytesAsync(path,ct)):null;
            bool deletion=item.Action==PlaylistTransactionAction.PendingZidooDelete;
            if(!deletion&&!prepared.ContainsKey(item.FileName))throw new InvalidDataException("MediaMonkey changed during planning; prepare again.");
            review.Add(new(item.FileName,deletion?0:counts[item.FileName],oldHash,deletion?null:Hash(ContentBytes(prepared[item.FileName].Lines)),deletion?"Delete":item.Action.ToString(),File.Exists(path)?NativeProductionTransaction.Visibility(path):null));
        }
        return review.AsReadOnly();
    }
    public async Task<IReadOnlyList<ReviewedChange>> PrepareRepairCandidatesAsync(AppConfig config,CancellationToken ct=default)
    {
        ConfigurationService.Validate(config);
        var validation=await new MediaMonkeyPlaylistExporter().ValidateAsync(config,ct);
        var candidates=new List<ReviewedChange>();
        foreach(var playlist in validation.PreparedPlaylists)
        {
            string path=Path.Combine(config.NAS.PlaylistFolderUNC,playlist.M3UFile);
            int count=validation.PlaylistMap.Single(x=>x.M3UFile==playlist.M3UFile).TrackCount;
            NativeInterruptedRecovery.NoLink(path);
            if(count<=0 || !File.Exists(path))continue;
            candidates.Add(new(playlist.M3UFile,count,Hash(await File.ReadAllBytesAsync(path,ct)),Hash(ContentBytes(playlist.Lines)),
                "Repair",NativeProductionTransaction.Visibility(path),true));
        }
        return candidates.OrderBy(x=>x.FileName,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static void AddReviewedRepairs(string root, IReadOnlyList<ReviewedChange>? reviewed,
        IReadOnlyDictionary<string,(byte[] Content,int Count)> authoritative, List<Work> changes)
    {
        if(reviewed is null)return;
        if(reviewed.Select(x=>x.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=reviewed.Count)
            throw new InvalidDataException("Duplicate reviewed playlist changes.");
        foreach(var item in reviewed.Where(x=>x.ReimportExisting))
        {
            if(!authoritative.TryGetValue(item.FileName,out var expected) || expected.Count<=0 || item.ExpectedCount!=expected.Count ||
                item.OriginalSHA256 is null || item.CandidateSHA256!=Hash(expected.Content))
                throw new InvalidDataException("Playlist repair differs from the reviewed MediaMonkey export: "+item.FileName);
            string path=Path.Combine(root,item.FileName);NativeInterruptedRecovery.NoLink(path);
            if(!File.Exists(path) || Hash(File.ReadAllBytes(path))!=item.OriginalSHA256 ||
                item.OriginalVisibility is null || NativeProductionTransaction.Visibility(path)!=item.OriginalVisibility)
                throw new InvalidDataException("Playlist file or visibility changed since repair review: "+item.FileName);
            changes.RemoveAll(x=>x.FileName.Equals(item.FileName,StringComparison.OrdinalIgnoreCase));
            changes.Add(new(item.FileName,expected.Content,expected.Count,item.OriginalSHA256,false,true));
        }
    }
    public static async Task<IReadOnlyList<Exception>> ApplyTransactionsAsync(
        IEnumerable<NativeProductionTransaction.Change> changes,
        Func<NativeProductionTransaction.Change,CancellationToken,Task> apply,
        Action<NativeProductionTransaction.Change> onStart,Action<NativeProductionTransaction.Change> onRestored,CancellationToken ct,
        Action<NativeProductionTransaction.Change>? onCompleted = null)
    {
        var failures=new List<Exception>();
        foreach(var item in changes)
        {
            ct.ThrowIfCancellationRequested();onStart(item);
            try{await apply(item,ct);onCompleted?.Invoke(item);}
            catch(NativeTransactionRolledBackException error) when(!ct.IsCancellationRequested)
            {failures.Add(error);onRestored(item);}
        }
        return failures.AsReadOnly();
    }
    public static IReadOnlyList<string> MissingManagedPlaylists(IEnumerable<(string FileName,int TrackCount)> expected,
        IReadOnlyList<ZidooPlaylistRecord> inventory,IEnumerable<string> planned)
    {
        var scheduled=new HashSet<string>(planned,StringComparer.OrdinalIgnoreCase);
        return expected.Where(item=>item.TrackCount>0 && !scheduled.Contains(item.FileName) &&
            !inventory.Any(row=>row.Name==Path.GetFileNameWithoutExtension(item.FileName)))
            .Select(item=>item.FileName).ToArray();
    }
    public async Task<int> RunAsync(AppConfig config, CancellationToken ct = default,
        IReadOnlyList<ReviewedChange>? reviewed = null,IReadOnlyList<ZidooPlaylistRecord>? reviewedInventory = null)
    {
        LastRunVerifiedDevice=false;LastRunCompletedChanges=0;LastRunRecovered=false;
        ConfigurationService.Validate(config);
        string root = Path.GetFullPath(config.NAS.PlaylistFolderUNC);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Playlist root is a link; execution refused.");
        string lockPath = Path.Combine(root,".mmzs-production.lock");
        if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Transaction lock is a link.");
        using var fileLock = new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(File.Exists(Path.Combine(root,".mmzs-production-pending.json")) || File.Exists(NativeProductionRunState.Marker(root)))
        {
            await RecoverInterruptedRunUnderLockAsync(config,root,ct);
            LastRunRecovered=true;return 0;
        }
        var exporter = new MediaMonkeyPlaylistExporter();
        var prepared = await exporter.ValidateAsync(config,ct);
        var existing = FrozenPlaylistFiles.Enumerate(root).ToDictionary(path=>Path.GetFileName(path),StringComparer.OrdinalIgnoreCase);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<Work>();
        foreach(var playlist in prepared.PreparedPlaylists)
        {
            if(!expected.Add(playlist.M3UFile))throw new InvalidDataException("Duplicate authoritative playlist filename.");
            byte[] content = ContentBytes(playlist.Lines);
            string? oldHash=null;
            bool inBaseline=existing.TryGetValue(playlist.M3UFile,out var oldPath);
            oldPath??=Path.Combine(root,playlist.M3UFile);
            // Discovery follows the visible frozen baseline. Physical files still require
            // a reviewed hash and backup, including hidden files at a candidate's target.
            if(File.Exists(oldPath))
            {
                if((File.GetAttributes(oldPath)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked playlist refused.");
                var old=await File.ReadAllBytesAsync(oldPath,ct);oldHash=Hash(old);
                // The frozen orchestrator compares SHA-256 of exact staged/baseline bytes.
                if(inBaseline && string.Equals(oldHash,Hash(content),StringComparison.Ordinal))continue;
            }
            int count=prepared.PlaylistMap.Single(x=>x.M3UFile==playlist.M3UFile).TrackCount;
            changes.Add(new(playlist.M3UFile,content,count,oldHash,!inBaseline));
        }
        foreach(var entry in existing.Where(x=>!expected.Contains(x.Key!)).OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase))
        {
            if((File.GetAttributes(entry.Value)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked stale playlist refused.");
            changes.Add(new(entry.Key!,null,0,Hash(await File.ReadAllBytesAsync(entry.Value,ct))));
        }
        AddReviewedRepairs(root,reviewed,prepared.PreparedPlaylists.ToDictionary(x=>x.M3UFile,
            x=>(ContentBytes(x.Lines),prepared.PlaylistMap.Single(row=>row.M3UFile==x.M3UFile).TrackCount),StringComparer.OrdinalIgnoreCase),changes);
        if(reviewed is not null && (changes.Count!=reviewed.Count || changes.Any(change=>
            !reviewed.Any(item=>item.FileName==change.FileName && item.ExpectedCount==change.Count && item.OriginalSHA256==change.OldHash && item.ReimportExisting==change.ReimportExisting &&
                (item.OriginalVisibility is null || item.OriginalVisibility==(File.Exists(Path.Combine(root,change.FileName))?NativeProductionTransaction.Visibility(Path.Combine(root,change.FileName)):(int?)null)) &&
                (item.CandidateSHA256 is null || (change.Content is not null && item.CandidateSHA256==Hash(change.Content)))))))
            throw new InvalidDataException("Live source/NAS state differs from the reviewed plan. Nothing synchronized.");
        if(changes.Count==0){Output?.Invoke("No playlist file changes; Zidoo was not contacted or awakened.");return 0;}
        string evidence=Path.Combine(Environment.ExpandEnvironmentVariables(config.Application.ReportRoot),"NativeSync-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence,"plan.json"),JsonSerializer.Serialize(changes.Select(x=>new{x.FileName,Action=x.ReimportExisting?"Repair":x.Content is null?"Delete":x.IsCreate?"Create":"Update",x.Count,x.OldHash}),new JsonSerializerOptions{WriteIndented=true}),ct);
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false};
        using var http=new HttpClient(handler){BaseAddress=new UriBuilder("http",config.Zidoo.IPAddress,config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        var device=new NativeZidooPlaylistDevice(http,config);device.Output+=message=>Output?.Invoke(message);
        var power=new NativeZidooConnectivityPowerService(http);
        bool initiallyReachable=await Reachable(config,ct);
        new NativeProductionRunState(1,root,Path.GetFullPath(evidence),config.Zidoo.IPAddress,config.Zidoo.ApiPort,
            config.Zidoo.ExpectedModel,config.Zidoo.MacAddress,initiallyReachable).Save(root);
        bool powerRestored=true;
        bool woke=false;
        Exception? operationFailure=null;
        try
        {
            if(!initiallyReachable){woke=true;await WakeAndWaitAsync(config,power,device,evidence,ct);}
            await device.VerifyIdentityAsync(ct);
            var initialInventory=await device.InventoryAsync(ct);
            if(reviewedInventory is not null && !initialInventory.Select(x=>(x.Id,x.Name,x.Count)).OrderBy(x=>x.Id,StringComparer.Ordinal)
                .SequenceEqual(reviewedInventory.Select(x=>(x.Id,x.Name,x.Count)).OrderBy(x=>x.Id,StringComparer.Ordinal)))
                throw new InvalidDataException("Device inventory changed since review; no playlist mutation performed.");
            await device.PreflightDirectoriesAsync(ct);
            // Match the preserved wrapper: only after a changed run has connected,
            // repair populated managed playlists absent from the device.
            foreach(string filename in MissingManagedPlaylists(prepared.PlaylistMap.Select(item=>(item.M3UFile,item.TrackCount)),initialInventory,changes.Select(item=>item.FileName)))
            {
                var playlist=prepared.PreparedPlaylists.Single(item=>item.M3UFile==filename);
                byte[] old=await File.ReadAllBytesAsync(Path.Combine(root,filename),ct);
                changes.Add(new(filename,ContentBytes(playlist.Lines),prepared.PlaylistMap.Single(item=>item.M3UFile==filename).TrackCount,Hash(old)));
                Output?.Invoke("Restore missing playlist: "+filename);
            }
            // Refuse count-only hidden-file creations before any playlist transaction starts.
            foreach(var change in changes)
                NativeProductionTransaction.CheckExistingCreation(new(change.FileName,change.Content,change.Count,change.OldHash,change.IsCreate,change.ReimportExisting),
                    initialInventory.SingleOrDefault(x=>x.Name==Path.GetFileNameWithoutExtension(change.FileName)),change.OldHash is not null);
            string baselineFolder=Path.Combine(evidence,"Baseline");Directory.CreateDirectory(baselineFolder);
            var baselineFiles=new List<object>();
            // Keep the frozen visible-file baseline for planning; additionally protect
            // physical target files (including hidden files) before any mutation.
            var recoverySources=RecoverySources(root,existing,changes.Where(x=>x.OldHash is not null).Select(x=>(x.FileName,x.OldHash!)));
            foreach(var entry in recoverySources)
            {
                byte[] content=await File.ReadAllBytesAsync(entry.Value,ct);
                string backup=Path.Combine(baselineFolder,entry.Key);
                using(var stream=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough))
                {await stream.WriteAsync(content,ct);stream.Flush(true);}
                baselineFiles.Add(new{FileName=entry.Key,SHA256=Hash(content)});
            }
            await File.WriteAllTextAsync(Path.Combine(evidence,"baseline-inventory.json"),JsonSerializer.Serialize(initialInventory),ct);
            await File.WriteAllTextAsync(Path.Combine(evidence,"baseline-files.json"),JsonSerializer.Serialize(baselineFiles),ct);
            foreach(var change in changes)
            {
                var original=initialInventory.SingleOrDefault(x=>x.Name==Path.GetFileNameWithoutExtension(change.FileName));
                if(original is not null && !(change.OldHash is null && change.Content is not null && change.Count > 0 && original.Count == change.Count))
                {
                    var backup=Path.Combine(baselineFolder,change.FileName);
                    if(!File.Exists(backup) || File.ReadAllLines(backup).Count(line=>!string.IsNullOrWhiteSpace(line)&&!line.StartsWith('#'))!=original.Count)
                        throw new InvalidDataException("Original file cannot reproduce device track count: "+change.FileName);
                }
            }
            var transaction=new NativeProductionTransaction(device);
            var compensatedFailures=await ApplyTransactionsAsync(
                changes.Select(item=>new NativeProductionTransaction.Change(item.FileName,item.Content,item.Count,item.OldHash,item.IsCreate,item.ReimportExisting)),
                (item,token)=>transaction.ApplyAsync(root,evidence,item,token),
                item=>Output?.Invoke("Starting "+(item.ReimportExisting?"repair":item.NewContent is null?"delete":item.IsCreate?"create":"update")+" (not yet completed): "+item.FileName),
                item=>Output?.Invoke("Could not synchronize "+item.FileName+". Its previous state was restored and verified. Continuing with the remaining playlists."),ct,
                item=>Output?.Invoke("Completed and verified: "+item.FileName));
            var finalInventory=await device.InventoryAsync(ct);
            if(compensatedFailures.Count>0)
            {
                await File.WriteAllTextAsync(Path.Combine(evidence,"partially-completed.json"),JsonSerializer.Serialize(new{Status="FAILED_WITH_VERIFIED_ROLLBACK",Failed=compensatedFailures.Count,Transactions=changes.Count,FinalInventory=finalInventory}),CancellationToken.None);
                throw new NativeTransactionRolledBackException("Some playlists could not be synchronized and were restored. Other completed changes are kept.",new AggregateException(compensatedFailures));
            }
            foreach(var item in prepared.PlaylistMap)
            {
                var persistent=finalInventory.SingleOrDefault(x=>x.Name==Path.GetFileNameWithoutExtension(item.M3UFile));
                if(item.TrackCount==0 ? persistent is not null : persistent?.Count!=item.TrackCount)
                    throw new InvalidDataException("Final managed playlist reconciliation failed: "+item.M3UFile);
            }
            await File.WriteAllTextAsync(Path.Combine(evidence,"final-inventory.json"),JsonSerializer.Serialize(finalInventory),ct);

        }
        catch(Exception error){operationFailure=error;throw;}
        finally
        {
            if(woke)
            {
                try
                {
                    await RestoreOffAsync(config,http,device);
                }
                catch(Exception error)
                {
                    powerRestored=false;
                    await File.WriteAllTextAsync(Path.Combine(evidence,"power-restore-failure.json"),JsonSerializer.Serialize(new{Error=error.ToString()}));
                    throw new IOException("Original power state could not be verified. Review the saved run details before continuing.", operationFailure is null ? error : new AggregateException(operationFailure,error));
                }
            }
            if(powerRestored && !File.Exists(Path.Combine(root,".mmzs-production-pending.json")))File.Delete(NativeProductionRunState.Marker(root));
            Output?.Invoke("Run evidence: "+evidence);
        }
        await File.WriteAllTextAsync(Path.Combine(evidence,"completed.json"),JsonSerializer.Serialize(new{Status="PASS",Transactions=changes.Count,PowerRestored=true,Time=DateTimeOffset.Now}),ct);
        LastRunVerifiedDevice=true;LastRunCompletedChanges=changes.Count;
        return 0;
    }

    private async Task RecoverInterruptedRunUnderLockAsync(AppConfig config,string root,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var state=NativeProductionRunState.Read(config,root);
        string pendingPath=Path.Combine(root,".mmzs-production-pending.json");
        bool pending=File.Exists(pendingPath);
        if(pending)
        {
            var receipt=NativeInterruptedRecovery.ReadPending(root,Environment.ExpandEnvironmentVariables(config.Application.ReportRoot));
            if(!string.Equals(Path.GetDirectoryName(receipt.Journal),state.Evidence,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Transaction and run recovery records differ.");
        }
        byte[] stateBytes=File.ReadAllBytes(NativeProductionRunState.Marker(root));
        Output?.Invoke("Recovering interrupted synchronization. Please wait while the previous run is verified.");
        using var recovery=new CancellationTokenSource(TimeSpan.FromMinutes(6));
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false};
        using var http=new HttpClient(handler){BaseAddress=new UriBuilder("http",config.Zidoo.IPAddress,config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        var device=new NativeZidooPlaylistDevice(http,config);device.Output+=message=>Output?.Invoke(message);var power=new NativeZidooConnectivityPowerService(http);
        bool verified=false;Exception? failure=null;string outcome="Power state restored";
        try
        {
            if(pending || state.InitiallyReachable)
            {
                if(!await Reachable(config,recovery.Token))await WakeAndWaitAsync(config,power,device,state.Evidence,recovery.Token);
                await device.VerifyIdentityAsync(recovery.Token);
                if(pending)
                {
                    await device.PreflightDirectoriesAsync(recovery.Token);
                    outcome=await new NativeInterruptedRecovery(device).RecoverUnderLockAsync(root,
                        Environment.ExpandEnvironmentVariables(config.Application.ReportRoot),recovery.Token);
                }
            }
            verified=true;
        }
        catch(Exception error){failure=error;throw;}
        finally
        {
            if(!state.InitiallyReachable)
            {
                try{await RestoreOffAsync(config,http,device);}
                catch(Exception error){verified=false;throw new IOException("Recovery could not verify the previous power state. Recovery evidence was retained.",failure is null?error:new AggregateException(failure,error));}
            }
        }
        if(verified)
        {
            if(Hash(File.ReadAllBytes(NativeProductionRunState.Marker(root)))!=Hash(stateBytes))throw new IOException("Interrupted run marker changed; retained.");
            NativeInterruptedRecovery.Save(Path.Combine(state.Evidence,"run-recovered.json"),JsonSerializer.SerializeToUtf8Bytes(new{Status="PASS",Outcome=outcome,PowerRestored=true,Time=DateTimeOffset.Now}));
            File.Delete(NativeProductionRunState.Marker(root));
            Output?.Invoke("Interrupted synchronization recovered and verified. Validate and review the current playlists before starting another synchronization.");
        }
    }

    private async Task RestoreOffAsync(AppConfig config,HttpClient http,NativeZidooPlaylistDevice device)
    {
        using var restore=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        if(await Reachable(config,restore.Token))
        {
            await device.VerifyIdentityAsync(restore.Token);
            using var response=await http.GetAsync("/ZidooControlCenter/RemoteControl/sendkey?key=Key.PowerOn.Poweroff",restore.Token);
            response.EnsureSuccessStatusCode();
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(restore.Token));
            if(!json.RootElement.TryGetProperty("status",out var status)||status.GetInt32()!=200)throw new IOException("Power-off was not acknowledged.");
            do{await Task.Delay(2000,restore.Token);}while(await Reachable(config,restore.Token));
        }
        Output?.Invoke("Original unreachable/off state restored: control port is no longer reachable.");
    }
    private async Task WakeAndWaitAsync(AppConfig config,NativeZidooConnectivityPowerService power,NativeZidooPlaylistDevice device,string evidence,CancellationToken ct)
    {
                Output?.Invoke("Zidoo control port is unreachable. Sending Wake-on-LAN to the configured device.");
                var attempts=new List<DateTimeOffset>();
                try
                {
                    await NativeZidooReadiness.WaitAsync(device.VerifyIdentityAsync,async token=>
                    {
                        await power.SendWakeOnLanAsync(config.Zidoo.MacAddress,config.Zidoo.BroadcastAddress,config.Zidoo.WolPort,token);
                        attempts.Add(DateTimeOffset.Now);
                        NativeInterruptedRecovery.Save(Path.Combine(evidence,"wake-attempt.json"),JsonSerializer.SerializeToUtf8Bytes(new{
                            config.Zidoo.IPAddress,config.Zidoo.ApiPort,config.Zidoo.MacAddress,config.Zidoo.BroadcastAddress,config.Zidoo.WolPort,
                            config.Timing.ZidooReadinessTimeoutSeconds,Attempts=attempts,
                            Scope="UDP sends completed; receipt and physical power state are not established"}));
                    },TimeSpan.FromSeconds(config.Timing.ZidooReadinessTimeoutSeconds),remaining=>
                        Output?.Invoke("Waiting for Zidoo model/API readiness ("+Math.Max(0,(int)remaining.TotalSeconds)+" seconds remaining)."),ct);
                }
                catch(TimeoutException error)
                {
                    NativeInterruptedRecovery.Save(Path.Combine(evidence,"readiness-failure.json"),JsonSerializer.SerializeToUtf8Bytes(new{LastError=error.ToString(),Time=DateTimeOffset.Now}));
                    throw;
                }
                Output?.Invoke("Zidoo identity verified. Allowing "+config.Timing.ColdBootUiStabilizationSeconds+" seconds for its music interface to stabilize.");
                await Task.Delay(TimeSpan.FromSeconds(config.Timing.ColdBootUiStabilizationSeconds),ct);

    }

    private static async Task<bool> Reachable(AppConfig config,CancellationToken ct)
    {
        using var socket=new TcpClient();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try{await socket.ConnectAsync(config.Zidoo.IPAddress,config.Zidoo.ApiPort,timeout.Token);return true;}
        catch(SocketException){return false;}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return false;}
    }
}
