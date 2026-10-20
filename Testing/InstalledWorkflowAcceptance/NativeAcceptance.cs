using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

internal static partial class Program
{
    private static async Task CleanupOwnedFixture(string recipe)
    {
        fixture=JsonSerializer.Deserialize<Fixture>(File.ReadAllBytes(recipe))??throw new InvalidDataException("Owned fixture missing.");
        string root=fixture.Config.NAS.PlaylistFolderUNC;
        if(!OwnedFolderName(root) || fixture.Name!="MMZS Acceptance "+fixture.Nonce ||
            string.Equals(root,fixture.SourceFolder,StringComparison.OrdinalIgnoreCase) ||
            Hash(File.ReadAllBytes(typeof(MediaMonkeyZidooSync.MainWindow).Assembly.Location))!=fixture.CandidateSHA256 ||
            !File.ReadAllText(Path.Combine(root,".mmzs-acceptance-owner.json")).Contains(fixture.Nonce,StringComparison.Ordinal))
            throw new InvalidDataException("Exact owned test fixture required.");
        using var sourceLease=new FileStream(Path.Combine(fixture.SourceFolder,".mmzs-production.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(File.Exists(Path.Combine(root,".mmzs-production-pending.json")) || File.Exists(NativeProductionRunState.Marker(root)))
            throw new IOException("Recovery must be reviewed before cleanup.");
        if(!await Reachable(fixture.Config))throw new IOException("Device must already be on for reviewed cleanup; no wake retry performed.");
        var inventory=await Inventory(fixture.Config);
        Check(inventory.Where(row=>row.Name!=fixture.Name).Select(row=>(row.Id,row.Name,row.Count)).OrderBy(row=>row.Id,StringComparer.Ordinal)
            .SequenceEqual(fixture.Baseline.Select(row=>(row.Id,row.Name,row.Count)).OrderBy(row=>row.Id,StringComparer.Ordinal)),"Existing device inventory preserved before cleanup");
        var own=inventory.SingleOrDefault(row=>row.Name==fixture.Name);
        if(own is not null && own.Count is not (1 or 2))throw new InvalidDataException("Unexpected owned playlist state; cleanup refused.");
        SetTracks(-1);var runner=new SyncRunner(new NativeProductionSyncService());runner.Output+=entry=>Console.WriteLine(entry.Message);
        var review=await runner.PrepareReviewAsync(fixture.Config);
        if(review.Count!=1 || review[0].FileName!=fixture.Name+".m3u")throw new InvalidDataException("Cleanup must contain only the owned playlist deletion.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
        if(await runner.RunAsync(fixture.Config,timeout.Token,review)!=0)throw new IOException("Owned cleanup did not complete.");
        var after=await Inventory(fixture.Config);
        Check(after.Select(row=>(row.Id,row.Name,row.Count)).OrderBy(row=>row.Id,StringComparer.Ordinal)
            .SequenceEqual(fixture.Baseline.Select(row=>(row.Id,row.Name,row.Count)).OrderBy(row=>row.Id,StringComparer.Ordinal)),"Original device inventory restored after cleanup");
        VerifyAndClean();
        WriteEvidence(Path.Combine(fixture.Root,"reviewed-cleanup.json"),new{Status="PASS",DeviceLeftOn=true,Scope="Owned disposable playlist and NAS mirror removed; failed wake acceptance remains failed",Time=DateTimeOffset.Now});
    }
    private static bool OwnedFolderName(string path)=>Path.GetFileName(path)=="MMZS-Test-"+fixture.Nonce ||
        Path.GetFileName(path)=="00-MMZS-Test-"+fixture.Nonce;
    private static void WriteEvidence(string path,object value)
    {
        string stage=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        using(var stream=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough))
        {stream.Write(JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions{WriteIndented=true}));stream.Flush(true);}
        try{if(File.Exists(path))File.Replace(stage,path,null);else File.Move(stage,path);}
        finally{if(File.Exists(stage))File.Delete(stage);}
    }
    /// <summary>No Application, Window, dispatcher pump, capture or UI automation is created in this mode.</summary>
    private static async Task NativeAcceptance(string settingsPath,string results,string expectedHash,string? preparedRecipe=null)
    {
        bool? initial=null;bool successful=false;FileStream? sourceLease=null;
        try
        {
            fixture=preparedRecipe is null?Prepare(settingsPath,results,expectedHash):JsonSerializer.Deserialize<Fixture>(File.ReadAllBytes(preparedRecipe))??throw new InvalidDataException("Prepared fixture missing.");
            if(preparedRecipe is not null)ValidatePreparedFixture(expectedHash);
            Console.WriteLine("Native live evidence: "+fixture.Root);
            sourceLease=new FileStream(Path.Combine(fixture.SourceFolder,".mmzs-production.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            SetTracks(2);initial=await Reachable(fixture.Config);
            WriteEvidence(Path.Combine(fixture.Root,"power-ownership.json"),new{InitiallyReachable=initial,WakePlanned=!initial.Value,Time=DateTimeOffset.Now});
            WriteEvidence(Path.Combine(fixture.Root,"fixture.json"),fixture);
            await On(fixture.Config);
            var currentBaseline=await Inventory(fixture.Config);
            if(preparedRecipe is not null&&!fixture.Baseline.Select(row=>(row.Id,row.Name,row.Count)).SequenceEqual(currentBaseline.Select(row=>(row.Id,row.Name,row.Count))))
                throw new InvalidDataException("Device differs from the explicitly prepared acceptance baseline; no test mutation started.");
            fixture=fixture with{Baseline=currentBaseline};
            WriteEvidence(Path.Combine(fixture.Root,"fixture.json"),fixture);
            Check(!fixture.Baseline.Any(row=>row.Name==fixture.Name),"Disposable device playlist name is unused");
            var source=JsonSerializer.Deserialize<AppConfig>(File.ReadAllBytes(settingsPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            source.Application.ReportRoot=Path.Combine(fixture.Root,"SourceValidation");
            var baseline=await new MediaMonkeyPlaylistExporter().ValidateAsync(source);
            var deferred=DeferredManagedNames();
            Check(baseline.PlaylistMap.Where(item=>!deferred.Contains(Path.GetFileNameWithoutExtension(item.M3UFile))).All(item=>item.TrackCount==0 ? !fixture.Baseline.Any(row=>row.Name==Path.GetFileNameWithoutExtension(item.M3UFile)):
                fixture.Baseline.SingleOrDefault(row=>row.Name==Path.GetFileNameWithoutExtension(item.M3UFile))?.Count==item.TrackCount),"Existing managed playlists need no unrelated device repair");
            var service=new NativeProductionSyncService();var runner=new SyncRunner(service);
            var events=new ConcurrentQueue<LogEvent>();runner.Output+=entry=>{events.Enqueue(entry);Console.WriteLine(entry.Message);};

            async Task Execute(string step,int changes,bool cancel=false,bool recovering=false)
            {
                while(events.TryDequeue(out _)){}
                var review=await runner.PrepareReviewAsync(fixture.Config);
                Check(review.Count==changes && review.All(item=>item.FileName==fixture.Name+".m3u"),"Reviewed changes confined to owned playlist: "+step);
                WriteEvidence(Path.Combine(fixture.Root,"current-step.json"),new{Step=step,Reviewed=review,Time=DateTimeOffset.Now});
                using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(8));bool requested=false;
                void Cancel(LogEvent entry){if(cancel && entry.Message=="Update: "+fixture.Name+".m3u"){requested=true;cancellation.CancelAfter(TimeSpan.FromSeconds(1));}}
                runner.Output+=Cancel;
                try
                {
                    int result=await runner.RunAsync(fixture.Config,cancellation.Token,review);
                    Check(!cancel && result==0 && runner.LastRunRecovered==recovering,"Native expected completion: "+step);
                }
                catch(OperationCanceledException)when(cancel && requested)
                {Check(UserMessages.Cancelled().Level==LogLevel.Yellow,"Intentional cancellation completed after compensation");}
                finally{runner.Output-=Cancel;WriteEvidence(Path.Combine(fixture.Root,step+"-output.json"),events.ToArray());}
                if(!cancel)Check(UserMessages.Completed(runner.LastRunCompletedChanges).Level==LogLevel.Green,"Verified success presentation: "+step);
                Check(!File.Exists(Path.Combine(fixture.Config.NAS.PlaylistFolderUNC,".mmzs-production-pending.json")) &&
                    !File.Exists(NativeProductionRunState.Marker(fixture.Config.NAS.PlaylistFolderUNC)),"Transaction and power ownership reconciled: "+step);
            }
            async Task VerifyInventory(int? count)
            {
                var rows=await Inventory(fixture.Config);var owned=rows.SingleOrDefault(row=>row.Name==fixture.Name);
                Check(count is null?owned is null:owned?.Count==count,"Owned device playlist has expected count");
                Check(rows.Where(row=>row.Name!=fixture.Name).Select(row=>(row.Id,row.Name,row.Count)).OrderBy(row=>row.Id,StringComparer.Ordinal)
                    .SequenceEqual(fixture.Baseline.Select(row=>(row.Id,row.Name,row.Count)).OrderBy(row=>row.Id,StringComparer.Ordinal)),"Unrelated device identities/counts unchanged");
            }
            await Execute("create",1);await VerifyInventory(2);
            SetTracks(1);await Execute("update",1);await VerifyInventory(1);
            SetTracks(2);await Execute("cancel",1,cancel:true);await VerifyInventory(1);
            WriteEvidence(Path.Combine(fixture.Root,"current-step.json"),new{Step="real-process-interruption",Time=DateTimeOffset.Now});
            await KillOwnedWorkerWithoutDesktop();
            await Execute("recovery",0,recovering:true);await VerifyInventory(1);
            SetTracks(1);await Execute("unchanged",0);
            Check(events.Any(entry=>entry.Message.Contains("not contacted or awakened",StringComparison.Ordinal)),"No-change execution avoids device contact/wake");
            SetTracks(2);await Off(fixture.Config);await Execute("wake-update",1);
            Check(!await Reachable(fixture.Config),"Native wake path restored original off/unreachable state");
            Check(events.Any(entry=>entry.Message.Contains("Wake-on-LAN",StringComparison.Ordinal)),"Native wake path exercised");
            await Execute("unchanged-off",0);Check(!await Reachable(fixture.Config),"No-change execution leaves off device off");
            await On(fixture.Config);await VerifyInventory(2);
            SetTracks(0);await Execute("populated-to-empty",1);await VerifyInventory(null);
            string emptyFile=Path.Combine(fixture.Config.NAS.PlaylistFolderUNC,fixture.Name+".m3u");
            Check(File.Exists(emptyFile)&&!File.ReadLines(emptyFile).Any(line=>!string.IsNullOrWhiteSpace(line)&&!line.StartsWith('#')),"Empty managed playlist remains NAS-only");
            SetTracks(2);await Execute("empty-to-populated",1);await VerifyInventory(2);
            SetTracks(-1);await Execute("delete",1);await VerifyInventory(null);
            VerifyAndClean();successful=true;
        }
        finally
        {
            bool restored=false;
            try{if(fixture is not null && initial is not null){if(initial.Value)await On(fixture.Config);else await Off(fixture.Config);restored=true;}}
            catch(Exception error){successful=false;if(fixture is not null)WriteEvidence(Path.Combine(fixture.Root,"power-restore-failure.json"),new{Error=error.ToString()});}
            finally
            {
                if(fixture is not null)WriteEvidence(Path.Combine(fixture.Root,"native-completed.json"),new{Status=successful&&restored?"PASS":"FAIL",Checks=passed.Count,
                    CandidateSHA256=fixture.CandidateSHA256,Version="1.1-alpha.60.18.6",InitialReachable=initial,OriginalPowerRestored=restored,NoDesktopAutomation=true,SourceDatabaseReadOnly=true,Time=DateTimeOffset.Now});
                sourceLease?.Dispose();
            }
            if(successful && !restored)throw new IOException("Original power state was not restored.");
        }
        if(!successful)throw new IOException("Native acceptance did not complete; evidence retained.");
    }
    private static async Task KillOwnedWorkerWithoutDesktop()
    {
        string recipe=Path.Combine(fixture.Root,"fixture.json");WriteEvidence(recipe,fixture);
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--live-crash-worker");start.ArgumentList.Add(recipe);
        using var worker=Process.Start(start)??throw new IOException("Owned interruption worker did not start.");
        Task<string> error=worker.StandardError.ReadToEndAsync();Task<string> output=worker.StandardOutput.ReadToEndAsync();
        var deadline=DateTime.UtcNow.AddSeconds(100);
        while(!File.Exists(Path.Combine(fixture.Root,"crash-paused")))
        {
            if(worker.HasExited)throw new IOException(await error);
            if(DateTime.UtcNow>deadline){worker.Kill(true);await worker.WaitForExitAsync();throw new TimeoutException("Owned crash boundary not reached; inspect pending recovery before retrying.");}
            await Task.Delay(100);
        }
        worker.Kill(true);await worker.WaitForExitAsync();
        WriteEvidence(Path.Combine(fixture.Root,"interrupted-worker.json"),new{ExitCode=worker.ExitCode,Output=await output,Error=await error,Checkpoint="DeviceDeleted",Time=DateTimeOffset.Now});
        Check(true,"Owned live process terminated after verified test-playlist deletion");
    }
}
