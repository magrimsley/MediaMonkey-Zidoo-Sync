using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

// Local acceptance tool: the only authorized mutation is deletion of A New Test.
internal static class AutomatedDeleteWake
{
    internal static void ValidateReview(IReadOnlyList<NativeProductionSyncService.ReviewedChange> changes)
    {
        if(changes.Count!=1 || changes[0].FileName!="A New Test.m3u" ||
            changes[0].ExpectedCount!=0 || changes[0].OriginalSHA256 is null || changes[0].CandidateSHA256 is not null)
            throw new InvalidDataException("Authorized test requires only deletion of A New Test.m3u; other changes refused.");
    }

    private static async Task<bool> Reachable(AppConfig config,CancellationToken ct)
    {
        using var socket=new TcpClient();
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(3));
        try {await socket.ConnectAsync(config.Zidoo.IPAddress,config.Zidoo.ApiPort,limit.Token);return true;}
        catch(SocketException){return false;}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return false;}
    }

    public static async Task RunAsync(string configPath,string results)
    {
        var config=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ??throw new InvalidDataException("Configuration missing.");
        ConfigurationService.Validate(config);
        string evidence=Path.Combine(Path.GetFullPath(results),"AutomatedDeleteWake-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(evidence);
        config.Application.ReportRoot=evidence;
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(45));
        ConsoleCancelEventHandler cancel=(_,e)=>{e.Cancel=true;timeout.Cancel();};
        Console.CancelKeyPress+=cancel;
        try
        {
            var sync=new NativeProductionSyncService();
            var messages=new List<string>();
            sync.Output+=message=>{messages.Add(message);Console.WriteLine(message);File.AppendAllText(Path.Combine(evidence,"run.log"),message+Environment.NewLine);};
            Console.WriteLine("Waiting for removal of only the A New Test playlist entry in MediaMonkey. Music files are not changed by this test.");
            IReadOnlyList<NativeProductionSyncService.ReviewedChange> review;
            do
            {
                review=await sync.PrepareReviewAsync(config,timeout.Token);
                if(review.Count!=0)break;
                await Task.Delay(TimeSpan.FromSeconds(10),timeout.Token);
            }while(true);
            ValidateReview(review);
            Console.WriteLine("MediaMonkey change detected: only A New Test.m3u deletion. Checking the device and off-state precondition.");
            string root=config.NAS.PlaylistFolderUNC;
            if(File.Exists(Path.Combine(root,".mmzs-production-pending.json")))throw new IOException("Pending recovery blocks this test.");
            string target=Path.Combine(root,"A New Test.m3u");
            if(File.ReadAllLines(target).Count(line=>!string.IsNullOrWhiteSpace(line)&&!line.StartsWith('#'))!=2)
                throw new InvalidDataException("A New Test is not the approved two-track fixture.");
            var files=Directory.EnumerateFiles(root,"*.m3u").ToDictionary(path=>Path.GetFileName(path)!,path=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),StringComparer.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(Path.Combine(evidence,"review.json"),JsonSerializer.Serialize(review),timeout.Token);
            using var handler=new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false};
            using var http=new HttpClient(handler){BaseAddress=new UriBuilder("http",config.Zidoo.IPAddress,config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
            await File.WriteAllTextAsync(Path.Combine(evidence,"network-interfaces.json"),JsonSerializer.Serialize(
                System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Select(n=>new{
                    n.Name,Status=n.OperationalStatus.ToString(),Type=n.NetworkInterfaceType.ToString(),
                    Addresses=n.GetIPProperties().UnicastAddresses.Select(a=>a.Address.ToString()).ToArray()})),timeout.Token);
            var device=new NativeZidooPlaylistDevice(http,config);
            IReadOnlyList<ZidooPlaylistRecord>? beforeStandby=null;
            if(await Reachable(config,timeout.Token))
            {
                await device.VerifyIdentityAsync(timeout.Token);
                var inventory=await device.InventoryAsync(timeout.Token);
                beforeStandby=inventory;
                if(inventory.SingleOrDefault(x=>x.Name=="A New Test")?.Count!=2)throw new InvalidDataException("Approved device fixture differs.");
                await File.WriteAllTextAsync(Path.Combine(evidence,"before-standby-inventory.json"),JsonSerializer.Serialize(inventory),timeout.Token);
                Console.WriteLine("Putting the verified Zidoo into its normal off state for the authorized wake/restore test.");
                using var response=await http.GetAsync("/ZidooControlCenter/RemoteControl/sendkey?key=Key.PowerOn.Poweroff",timeout.Token);
                response.EnsureSuccessStatusCode();
                using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if(!doc.RootElement.TryGetProperty("status",out var status)||status.GetInt32()!=200)throw new IOException("Standby was not acknowledged.");
                using var off=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);off.CancelAfter(TimeSpan.FromSeconds(60));
                do{await Task.Delay(2000,off.Token);}while(await Reachable(config,off.Token));
            }
            // Require stable unreachable state before the application owns a wake/restore cycle.
            for(int i=0;i<3;i++){if(await Reachable(config,timeout.Token))throw new IOException("Off-state precondition unstable.");await Task.Delay(2000,timeout.Token);}
            if(await sync.RunAsync(config,timeout.Token,review,beforeStandby)!=0 || !sync.LastRunVerifiedDevice)throw new IOException("Synchronization did not verify the device.");
            if(await Reachable(config,timeout.Token))throw new IOException("Original off state was not restored.");
            if(!messages.Any(x=>x.StartsWith("Original unreachable/off state restored:",StringComparison.Ordinal)))throw new IOException("Wake/restore evidence missing.");
            var runs=Directory.GetDirectories(evidence,"NativeSync-*");
            if(runs.Length!=1)throw new IOException("Expected exactly one native synchronization receipt.");
            var baseline=JsonSerializer.Deserialize<ZidooPlaylistRecord[]>(File.ReadAllText(Path.Combine(runs[0],"baseline-inventory.json")))!;
            var final=JsonSerializer.Deserialize<ZidooPlaylistRecord[]>(File.ReadAllText(Path.Combine(runs[0],"final-inventory.json")))!;
            if(baseline.SingleOrDefault(x=>x.Name=="A New Test")?.Count!=2 || final.Any(x=>x.Name=="A New Test") ||
                !baseline.Where(x=>x.Name!="A New Test").Select(x=>(x.Id,x.Name,x.Count)).OrderBy(x=>x.Id,StringComparer.Ordinal)
                .SequenceEqual(final.Select(x=>(x.Id,x.Name,x.Count)).OrderBy(x=>x.Id,StringComparer.Ordinal)))
                throw new IOException("Device deletion or unrelated-playlist preservation failed.");
            var remaining=Directory.EnumerateFiles(root,"*.m3u").ToDictionary(path=>Path.GetFileName(path)!,path=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),StringComparer.OrdinalIgnoreCase);
            files.Remove("A New Test.m3u");
            if(File.Exists(target)||files.Count!=remaining.Count||files.Any(x=>!remaining.TryGetValue(x.Key,out string? hash)||hash!=x.Value))throw new IOException("NAS deletion or other-file preservation failed.");
            if(File.Exists(Path.Combine(root,".mmzs-production-pending.json")))throw new IOException("Recovery gate remains.");
            int messageStart=messages.Count;
            if(await sync.RunAsync(config,timeout.Token)!=0 ||sync.LastRunVerifiedDevice ||
                !messages.Skip(messageStart).Any(x=>x=="No playlist file changes; Zidoo was not contacted or awakened."))throw new IOException("Repeat-run no-change check failed.");
            if(await Reachable(config,timeout.Token))throw new IOException("No-change run left the device reachable.");
            var summary=new{Status="PASS",Scope="Native engine automated live acceptance; GUI interaction and TV/AVR state not assessed",Deleted="A New Test",OtherPlaylistsUnchanged=true,OtherM3UHashesUnchanged=true,WakeAndOffRestorationVerified=true,RepeatNoChange=true,ApplicationSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(NativeProductionSyncService).Assembly.Location))),Results=evidence};
            await File.WriteAllTextAsync(Path.Combine(evidence,"summary.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PASS: approved deletion, other-playlist preservation, wake/off restoration and repeat no-change verified. Results: "+evidence);
        }
        catch(Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{Status="FAIL",Error=error.ToString(),Results=evidence}));
            throw;
        }
        finally{Console.CancelKeyPress-=cancel;}
    }
}
