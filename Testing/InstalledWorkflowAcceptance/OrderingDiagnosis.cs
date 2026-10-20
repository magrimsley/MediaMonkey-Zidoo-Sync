using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using MediaMonkeyZidooSync.Services;

internal static partial class Program
{
    private static async Task DiagnoseColdOrdering(string recipe,string results,string expectedHash)
    {
        if(Hash(File.ReadAllBytes(typeof(MediaMonkeyZidooSync.MainWindow).Assembly.Location))!=expectedHash)
            throw new InvalidDataException("Published diagnostic payload hash differs.");
        var binding=JsonSerializer.Deserialize<NavigationRecipe>(File.ReadAllBytes(recipe))??throw new InvalidDataException("Missing binding.");
        ConfigurationService.Validate(binding.Config);
        string evidence=Path.Combine(Path.GetFullPath(results),"ColdOrderingSetup-Alpha60.18.6-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(evidence);
        bool initial=await Reachable(binding.Config),restored=false,finished=false;
        WriteEvidence(Path.Combine(evidence,"power-ownership.json"),new{InitiallyReachable=initial,CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
        try
        {
            if(initial)await Off(binding.Config);
            Console.WriteLine("Standby verified. Applying frozen single wake, readiness and UI stabilization.");
            await On(binding.Config);
            await DiagnoseOrdering(recipe,results,expectedHash);finished=true;
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
                    InitiallyReachable=initial,OriginalPowerRestored=restored,CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
                Console.WriteLine("Cold ordering setup evidence: "+evidence);
            }
        }
    }
    private static async Task DiagnoseOrdering(string recipe,string results,string expectedHash)
    {
        if(Hash(File.ReadAllBytes(typeof(MediaMonkeyZidooSync.MainWindow).Assembly.Location))!=expectedHash)
            throw new InvalidDataException("Published diagnostic payload hash differs.");
        var binding=JsonSerializer.Deserialize<NavigationRecipe>(File.ReadAllBytes(recipe))??throw new InvalidDataException("Missing binding.");
        var config=binding.Config;ConfigurationService.Validate(config);
        if(!binding.Target.Name.StartsWith("MMZS Acceptance ",StringComparison.Ordinal)||binding.Target.Count is <1 or >2)
            throw new InvalidDataException("Disposable target required.");
        string evidence=Path.Combine(Path.GetFullPath(results),"OrderingDiagnosis-Alpha60.18.6-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(evidence);
        var before=await Inventory(config);
        var target=before.Single(row=>row.Id==binding.Target.Id&&row.Name==binding.Target.Name&&row.Count==binding.Target.Count);
        WriteEvidence(Path.Combine(evidence,"before.json"),before);
        string[] anchor=Enumerable.Repeat("Key.Up",20).Concat(Enumerable.Repeat("Key.Down",2)).Concat(new[]{"Key.Ok","Key.Down"}).ToArray();
        string[] sort=new[]{"Key.Left","Key.Ok"}.Concat(Enumerable.Repeat("Key.Up",4)).Concat(Enumerable.Repeat("Key.Down",3))
            .Concat(new[]{"Key.Ok","Key.Up","Key.Ok","Key.Back"}).ToArray();
        string[] allowed=anchor.Concat(sort).Concat(new[]{"Key.Right"}).Concat(Enumerable.Repeat("Key.Down",target.GridIndex/config.Zidoo.PlaylistGridColumns))
            .Concat(Enumerable.Repeat("Key.Right",target.GridIndex%config.Zidoo.PlaylistGridColumns)).ToArray();
        WriteEvidence(Path.Combine(evidence,"binding.json"),new{CandidateSHA256=expectedHash,Target=target,AllowedKeys=allowed});
        using var guard=new OrderingGuard(allowed,evidence);
        using var http=new HttpClient(guard){BaseAddress=new UriBuilder("http",config.Zidoo.IPAddress,config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        try
        {
            var device=new NativeZidooPlaylistDevice(http,config);
            try{await device.DeleteExactAsync(target);throw new InvalidDataException("Diagnostic boundary missed.");}catch(NavigationBoundaryReached){}
            var after=await device.InventoryAsync(default);WriteEvidence(Path.Combine(evidence,"after.json"),after);
            bool unchanged=before.SequenceEqual(after);
            WriteEvidence(Path.Combine(evidence,"stopped.json"),new{Status=unchanged?"AWAITING_VISUAL_OBSERVATION":"FAIL",InventoryUnchanged=unchanged,
                guard.KeysForwarded,ExpectedKeys=allowed.Length,PlaylistActivationCommands=0,DeletionConfirmationCommands=0,CandidateSHA256=expectedHash,Time=DateTimeOffset.Now});
            if(!unchanged)throw new InvalidDataException("Inventory changed during ordering diagnosis.");
            Console.WriteLine("Stopped before playlist activation; ordered inventory unchanged. Evidence: "+evidence);
        }
        catch(Exception error){File.WriteAllText(Path.Combine(evidence,"diagnostic-error.log"),error.ToString());throw;}
    }
    private sealed class OrderingGuard:DelegatingHandler
    {
        private readonly string[] allowed;private readonly string evidence;
        private readonly Stopwatch timer=Stopwatch.StartNew();private readonly List<object> trace=new();private int opened;
        public int KeysForwarded{get;private set;}
        public OrderingGuard(string[] allowed,string evidence):base(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false}){this.allowed=allowed;this.evidence=evidence;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var uri=request.RequestUri??throw new InvalidDataException("Missing URI.");
            if(request.Method!=HttpMethod.Get)throw new InvalidDataException("Method refused.");
            string path=uri.AbsolutePath;string? key=null;
            if(path=="/ZidooControlCenter/RemoteControl/sendkey")
            {
                if(!uri.Query.StartsWith("?key=",StringComparison.Ordinal))throw new InvalidDataException("Key query refused.");
                key=Uri.UnescapeDataString(uri.Query[5..]);
                if(KeysForwarded==allowed.Length&&key=="Key.Ok")
                {
                    trace.Add(new{AtMs=timer.Elapsed.TotalMilliseconds,Route=uri.PathAndQuery,Forwarded=false,Reason="Stop before tile activation"});
                    WriteEvidence(Path.Combine(evidence,"trace.json"),trace);throw new NavigationBoundaryReached();
                }
                if(KeysForwarded>=allowed.Length||key!=allowed[KeysForwarded])throw new InvalidDataException("Unexpected key refused: "+key);
            }
            else if(path=="/ZidooControlCenter/Apps/openApp")
            {if(uri.Query!="?packageName=com.zidoo.audioplayer"||opened++>=1)throw new InvalidDataException("App launch refused.");}
            else if(path!="/ZidooControlCenter/getModel"&&path!="/ZidooMusicControl/v2/getSongLists")throw new InvalidDataException("Route refused.");
            double started=timer.Elapsed.TotalMilliseconds;
            try
            {
                var response=await base.SendAsync(request,ct);string body=await response.Content.ReadAsStringAsync(ct);
                int? status=null;using(var doc=JsonDocument.Parse(body))
                    if(doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.TryGetProperty("status",out var value)&&value.TryGetInt32(out int code))status=code;
                if(key is not null)KeysForwarded++;
                trace.Add(new{AtMs=started,CompletedMs=timer.Elapsed.TotalMilliseconds,Route=uri.PathAndQuery,Forwarded=true,HttpStatus=(int)response.StatusCode,ProtocolStatus=status});
                return response;
            }
            catch(Exception error){trace.Add(new{AtMs=started,Route=uri.PathAndQuery,Error=error.Message});throw;}
            finally{WriteEvidence(Path.Combine(evidence,"trace.json"),trace);}
        }
    }
}
