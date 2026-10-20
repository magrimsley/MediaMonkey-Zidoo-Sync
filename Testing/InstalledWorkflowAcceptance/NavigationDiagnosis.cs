using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

internal static partial class Program
{
    // Test-only guard around the exact published adapter. No transaction service,
    // WOL, import, deletion confirmation or arbitrary command is reachable here.
    private sealed record NavigationRecipe(AppConfig Config,ZidooPlaylistRecord Target);
    private static async Task DiagnoseColdNavigation(string phase,string recipe,string results,string expectedHash)
    {
        if(phase is not ("ready" or "home" or "first" or "target" or "opened" or "menu" or "confirmation"))throw new ArgumentException("Unknown diagnostic boundary.");
        if(Hash(File.ReadAllBytes(typeof(MediaMonkeyZidooSync.MainWindow).Assembly.Location))!=expectedHash)
            throw new InvalidDataException("Diagnostic adapter differs from reviewed payload.");
        var binding=JsonSerializer.Deserialize<NavigationRecipe>(File.ReadAllBytes(recipe))??throw new InvalidDataException("Diagnostic recipe missing.");
        ConfigurationService.Validate(binding.Config);
        if(!binding.Target.Name.StartsWith("MMZS Acceptance ",StringComparison.Ordinal)||binding.Target.Count is <1 or >2)
            throw new InvalidDataException("Disposable diagnostic target required.");
        string evidence=Path.Combine(Path.GetFullPath(results),"ColdNavigationSetup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(evidence);
        bool initial=await Reachable(binding.Config),finished=false,restored=false;
        WriteEvidence(Path.Combine(evidence,"power-ownership.json"),new{InitiallyReachable=initial,CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
        try
        {
            if(initial)
            {
                var before=await Inventory(binding.Config);
                if(!before.Any(row=>row.Id==binding.Target.Id&&row.Name==binding.Target.Name&&row.Count==binding.Target.Count))
                    throw new InvalidDataException("Disposable target differs before standby setup.");
                WriteEvidence(Path.Combine(evidence,"before-standby.json"),before);
                await Off(binding.Config);
            }
            Console.WriteLine("Standby verified. Applying frozen single wake, readiness and UI stabilization.");
            await On(binding.Config);
            if(phase=="ready")
            {
                var after=await Inventory(binding.Config);
                WriteEvidence(Path.Combine(evidence,"after-readiness.json"),after);
                if(initial)
                {
                    var before=JsonSerializer.Deserialize<ZidooPlaylistRecord[]>(File.ReadAllBytes(Path.Combine(evidence,"before-standby.json")))!;
                    if(!before.SequenceEqual(after))throw new InvalidDataException("Inventory changed during readiness-only diagnosis.");
                }
                Console.WriteLine("Stopped after readiness and stabilization; no app launch or navigation sent.");
            }
            else await DiagnoseNavigation(phase,recipe,results,expectedHash);
            finished=true;
        }
        catch(Exception error){File.WriteAllText(Path.Combine(evidence,"diagnostic-error.log"),error.ToString());throw;}
        finally
        {
            try
            {
                if(initial)await On(binding.Config);else await Off(binding.Config);
                restored=initial?await Reachable(binding.Config):!await Reachable(binding.Config);
                if(!restored)throw new IOException("Initial diagnostic power state was not restored.");
            }
            finally
            {
                WriteEvidence(Path.Combine(evidence,"completed.json"),new{Status=finished&&restored?"AWAITING_VISUAL_OBSERVATION":"FAIL",
                    InitiallyReachable=initial,OriginalPowerRestored=restored,Phase=phase,CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
                Console.WriteLine("Cold navigation setup evidence: "+evidence);
            }
        }
    }
    private static async Task DiagnoseReadiness(string recipe,string results,string expectedHash)
    {
        if(Hash(File.ReadAllBytes(typeof(MediaMonkeyZidooSync.MainWindow).Assembly.Location))!=expectedHash)
            throw new InvalidDataException("Diagnostic adapter differs from reviewed payload.");
        var binding=JsonSerializer.Deserialize<NavigationRecipe>(File.ReadAllBytes(recipe))??throw new InvalidDataException("Diagnostic recipe missing.");
        var config=binding.Config;
        ConfigurationService.Validate(config);
        string evidence=Path.Combine(Path.GetFullPath(results),"ReadinessDiagnosis-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(evidence);
        bool initial=await Reachable(config),wakeAttempted=false,ready=false,restored=false;
        WriteEvidence(Path.Combine(evidence,"power-ownership.json"),new{InitiallyReachable=initial,CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
        using var http=Client(config);
        var device=new NativeZidooPlaylistDevice(http,config);
        try
        {
            if(!initial)
            {
                wakeAttempted=true;
                await new NativeZidooConnectivityPowerService(http).SendWakeOnLanAsync(config.Zidoo.MacAddress,config.Zidoo.BroadcastAddress,config.Zidoo.WolPort);
                WriteEvidence(Path.Combine(evidence,"wake.json"),new{Time=DateTimeOffset.Now,Requests=1});
                Console.WriteLine("Wake-on-LAN sent. Waiting for the configured device.");
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(config.Timing.ZidooReadinessTimeoutSeconds));
                var attempts=new List<object>();
                while(true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    try
                    {
                        if(await Reachable(config))
                        {
                            using var probe=Client(config);probe.Timeout=TimeSpan.FromSeconds(5);
                            await new NativeZidooPlaylistDevice(probe,config).VerifyIdentityAsync(deadline.Token);
                            attempts.Add(new{Time=DateTimeOffset.Now,Status="IDENTITY_VERIFIED"});
                            WriteEvidence(Path.Combine(evidence,"readiness-attempts.json"),attempts);break;
                        }
                        attempts.Add(new{Time=DateTimeOffset.Now,Status="PORT_UNREACHABLE"});
                    }
                    catch(Exception error)when(error is HttpRequestException or TaskCanceledException)
                    {attempts.Add(new{Time=DateTimeOffset.Now,Status="NOT_READY",Message=error.Message});}
                    WriteEvidence(Path.Combine(evidence,"readiness-attempts.json"),attempts);
                    await Task.Delay(3000,deadline.Token);
                }
                Console.WriteLine("Device identity verified. Allowing the configured UI stabilization interval.");
                await Task.Delay(TimeSpan.FromSeconds(config.Timing.ColdBootUiStabilizationSeconds));
            }
            await device.VerifyIdentityAsync();
            WriteEvidence(Path.Combine(evidence,"inventory.json"),await device.InventoryAsync(default));
            ready=true;Console.WriteLine("Readiness verified; no playlist navigation or mutation sent.");
        }
        catch(Exception error)
        {File.WriteAllText(Path.Combine(evidence,"diagnostic-error.log"),error.ToString());throw;}
        finally
        {
            try
            {
                if(wakeAttempted)await Off(config);
                restored=initial?await Reachable(config):!await Reachable(config);
                if(!restored)throw new IOException("Initial power state was not restored.");
            }
            catch(Exception error)
            {File.WriteAllText(Path.Combine(evidence,"power-restoration-error.log"),error.ToString());throw;}
            finally
            {
                WriteEvidence(Path.Combine(evidence,"completed.json"),new{Status=ready&&restored?"PASS":"FAIL",InitiallyReachable=initial,
                    WakeAttempted=wakeAttempted,ReadinessVerified=ready,OriginalPowerRestored=restored,CandidateSHA256=expectedHash,
                    PlaylistNavigationCommands=0,PlaylistMutationCommands=0,Time=DateTimeOffset.Now});
                Console.WriteLine("Readiness evidence: "+evidence);
            }
        }
    }
    private static async Task DiagnoseNavigation(string phase,string recipe,string results,string expectedHash)
    {
        if(phase is not ("launch" or "home" or "first" or "target" or "opened" or "menu" or "confirmation"))throw new ArgumentException("Unknown navigation boundary.");
        if(Hash(File.ReadAllBytes(typeof(MediaMonkeyZidooSync.MainWindow).Assembly.Location))!=expectedHash)
            throw new InvalidDataException("Diagnostic adapter differs from reviewed payload.");
        var binding=JsonSerializer.Deserialize<NavigationRecipe>(File.ReadAllBytes(recipe))??throw new InvalidDataException("Diagnostic recipe missing.");
        var config=binding.Config;
        if(!binding.Target.Name.StartsWith("MMZS Acceptance ",StringComparison.Ordinal) || binding.Target.Count is <1 or >2)
            throw new InvalidDataException("Only an explicitly bound disposable test target is permitted.");
        ConfigurationService.Validate(config);
        string evidence=Path.Combine(Path.GetFullPath(results),"NavigationDiagnosis-Alpha60.18.4-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(evidence);
        using var guard=new NavigationGuard(phase,evidence);
        using var http=new HttpClient(guard){BaseAddress=new UriBuilder("http",config.Zidoo.IPAddress,config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        var device=new NativeZidooPlaylistDevice(http,config);
        try
        {
        await device.VerifyIdentityAsync();var before=await device.InventoryAsync(default);
        var target=before.Single(row=>row.Name==binding.Target.Name && row.Id==binding.Target.Id && row.Count==binding.Target.Count);
        WriteEvidence(Path.Combine(evidence,"before.json"),before);
        WriteEvidence(Path.Combine(evidence,"binding.json"),new{Phase=phase,CandidateSHA256=expectedHash,Target=target,GridColumns=config.Zidoo.PlaylistGridColumns,
            Scope="Actual published adapter navigation only; transport blocks before selected boundary; no transaction or power operations"});
        try{await device.DeleteExactAsync(target);throw new InvalidOperationException("Guard failed to stop navigation.");}
        catch(NavigationBoundaryReached){ }
        var after=await device.InventoryAsync(default);
        WriteEvidence(Path.Combine(evidence,"after.json"),after);
        bool unchanged=before.Select(row=>(row.Id,row.Name,row.Count)).SequenceEqual(after.Select(row=>(row.Id,row.Name,row.Count)));
        WriteEvidence(Path.Combine(evidence,"stopped.json"),new{Status=unchanged?"AWAITING_VISUAL_OBSERVATION":"FAIL",Phase=phase,InventoryUnchanged=unchanged,
            guard.ForwardedOk,ConfirmationKeysForwarded=0,CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
        if(!unchanged)throw new IOException("Inventory changed during guarded navigation. Stop all tests.");
        Console.WriteLine("Stopped before "+(phase=="launch"?"first Up after app launch":phase=="home"?"home Playlists OK":phase=="first"?"first grid-row movement":phase=="target"?"playlist-tile OK":phase=="opened"?"post-opening Left":phase=="menu"?"menu-item OK":"deletion-confirming Select")+"; ordered inventory unchanged. Evidence: "+evidence);
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(evidence,"diagnostic-error.log"),error.ToString());
            WriteEvidence(Path.Combine(evidence,"failed.json"),new{Status="FAIL",Phase=phase,
                Message=error.Message,guard.ForwardedOk,ConfirmationKeysForwarded=0,
                CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
            Console.Error.WriteLine("Navigation diagnosis stopped. Diagnostic details: "+evidence);
            throw;
        }
    }
    private sealed class NavigationBoundaryReached:Exception { }
    private sealed class NavigationGuard:DelegatingHandler
    {
        private readonly string phase,evidence;
        private readonly List<object> trace=new();
        private readonly Stopwatch timer=Stopwatch.StartNew();
        public int ForwardedOk{get;private set;}
        private int forwardedLeft;
        private int downsAfterHome;
        public NavigationGuard(string phase,string evidence):base(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false})
        {this.phase=phase;this.evidence=evidence;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var uri=request.RequestUri??throw new InvalidDataException("Missing diagnostic route.");
            if(request.Method!=HttpMethod.Get)throw new InvalidDataException("Diagnostic method refused.");
            string path=uri.AbsolutePath;string? key=null;
            bool read=path=="/ZidooControlCenter/getModel" || path=="/ZidooMusicControl/v2/getSongLists";
            bool open=path=="/ZidooControlCenter/Apps/openApp" && uri.Query=="?packageName=com.zidoo.audioplayer";
            if(path=="/ZidooControlCenter/RemoteControl/sendkey")
            {
                if(!uri.Query.StartsWith("?key=",StringComparison.Ordinal))throw new InvalidDataException("Unknown key query.");
                key=Uri.UnescapeDataString(uri.Query[5..]);
                if((phase=="launch" && key=="Key.Up") ||
                    (phase=="first" && ForwardedOk==1 && downsAfterHome>=1 && key is "Key.Down" or "Key.Right") ||
                    (key=="Key.Left" && phase=="opened") || (key=="Key.Select" && phase=="confirmation") ||
                    (key=="Key.Ok" && ForwardedOk>=(phase is "launch" or "home"?0:phase is "first" or "target"?1:phase=="opened"?2:phase=="menu"?3:4)))
                {
                    trace.Add(new{AtMs=timer.Elapsed.TotalMilliseconds,Route=uri.PathAndQuery,Forwarded=false,Reason="Authorized diagnostic boundary"});
                    WriteEvidence(Path.Combine(evidence,"trace.json"),trace);throw new NavigationBoundaryReached();
                }
                bool allowedLeft=key=="Key.Left" && (phase is "menu" or "confirmation") && ForwardedOk==2 && forwardedLeft==0;
                if(!allowedLeft && key is not ("Key.Up" or "Key.Down" or "Key.Right" or "Key.Ok"))
                    throw new InvalidDataException("Key refused by non-destructive diagnostic guard.");
            }
            else if(!read && !open)throw new InvalidDataException("Route refused by diagnostic guard.");
            double started=timer.Elapsed.TotalMilliseconds;
            trace.Add(new{AtMs=started,Route=uri.PathAndQuery,Attempted=true,State="REQUEST_STARTED"});
            WriteEvidence(Path.Combine(evidence,"trace.json"),trace);
            try
            {
            var response=await base.SendAsync(request,ct).ConfigureAwait(false);
            string body=await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            int? status=null;using(var json=JsonDocument.Parse(body))
                if(json.RootElement.ValueKind==JsonValueKind.Object && json.RootElement.TryGetProperty("status",out var value) && value.TryGetInt32(out int code))status=code;
            if(key=="Key.Ok")ForwardedOk++;
            if(key=="Key.Left")forwardedLeft++;
            if(key=="Key.Down" && ForwardedOk==1)downsAfterHome++;
            trace.Add(new{AtMs=started,CompletedMs=timer.Elapsed.TotalMilliseconds,Route=uri.PathAndQuery,Forwarded=true,HttpStatus=(int)response.StatusCode,ProtocolStatus=status});
            WriteEvidence(Path.Combine(evidence,"trace.json"),trace);
            return response;
            }
            catch(Exception error)
            {
                trace.Add(new{AtMs=started,CompletedMs=timer.Elapsed.TotalMilliseconds,
                    Route=uri.PathAndQuery,Attempted=true,State="REQUEST_FAILED",
                    ErrorType=error.GetType().FullName,Message=error.Message});
                WriteEvidence(Path.Combine(evidence,"trace.json"),trace);
                throw;
            }
        }
    }
}
