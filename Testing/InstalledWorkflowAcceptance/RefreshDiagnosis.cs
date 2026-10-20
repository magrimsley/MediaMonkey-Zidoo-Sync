using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using MediaMonkeyZidooSync.Services;
internal static partial class Program
{
    private static async Task DiagnoseRefresh(string recipe,string results,string expectedHash)
    {
        if(Hash(File.ReadAllBytes(typeof(MediaMonkeyZidooSync.MainWindow).Assembly.Location))!=expectedHash)throw new InvalidDataException("Published payload hash differs.");
        var binding=JsonSerializer.Deserialize<NavigationRecipe>(File.ReadAllBytes(recipe))??throw new InvalidDataException("Recipe missing.");
        ConfigurationService.Validate(binding.Config);
        string evidence=Path.Combine(Path.GetFullPath(results),"RefreshDiagnosis-Alpha60.18.6-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(evidence);
        var allowed=Enumerable.Repeat("Key.Up",20).Concat(Enumerable.Repeat("Key.Down",2)).Append("Key.Ok").ToArray();
        using var guard=new OrderingGuard(allowed,evidence);
        using var http=new HttpClient(guard){BaseAddress=new UriBuilder("http",binding.Config.Zidoo.IPAddress,binding.Config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        var device=new NativeZidooPlaylistDevice(http,binding.Config);
        try
        {
            await device.VerifyIdentityAsync();var before=await device.InventoryAsync(default);
            WriteEvidence(Path.Combine(evidence,"before.json"),before);
            var method=typeof(NativeZidooPlaylistDevice).GetMethod("RefreshPersistentPlaylistGridAfterMutationAsync",BindingFlags.Instance|BindingFlags.NonPublic)??throw new MissingMethodException("Actual refresh method missing.");
            await (Task)(method.Invoke(device,new object[]{before,CancellationToken.None})??throw new InvalidDataException("Missing refresh task."));
            var after=await device.InventoryAsync(default);WriteEvidence(Path.Combine(evidence,"after.json"),after);
            bool unchanged=before.SequenceEqual(after);
            WriteEvidence(Path.Combine(evidence,"completed.json"),new{Status=unchanged?"AWAITING_VISUAL_OBSERVATION":"FAIL",InventoryUnchanged=unchanged,guard.KeysForwarded,ExpectedKeys=allowed.Length,CandidateSHA256=expectedHash,DeletionCommands=0,ImportCommands=0,PowerCommands=0,Time=DateTimeOffset.Now});
            if(!unchanged||guard.KeysForwarded!=allowed.Length)throw new InvalidDataException("Refresh guard inventory/key check failed.");
            Console.WriteLine("Actual published refresh completed; inventory unchanged. Evidence: "+evidence);
        }
        catch(Exception error){File.WriteAllText(Path.Combine(evidence,"diagnostic-error.log"),error.ToString());throw;}
    }
}
