using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

try
{
    if(args.Length==1 && args[0]=="--test-automated-authorization")
    {
        AutomatedDeleteWake.ValidateReview(new[]{new NativeProductionSyncService.ReviewedChange("A New Test.m3u",0,"hash")});
        var invalid=new NativeProductionSyncService.ReviewedChange[][] {
            Array.Empty<NativeProductionSyncService.ReviewedChange>(),
            new[]{new NativeProductionSyncService.ReviewedChange("Coolio.m3u",0,"hash")},
            new[]{new NativeProductionSyncService.ReviewedChange("A New Test.m3u",2,"hash")},
            new[]{new NativeProductionSyncService.ReviewedChange("A New Test.m3u",0,null)},
            new[]{new NativeProductionSyncService.ReviewedChange("A New Test.m3u",0,"hash","candidate")},
            new[]{new NativeProductionSyncService.ReviewedChange("A New Test.m3u",0,"hash"),new NativeProductionSyncService.ReviewedChange("Other.m3u",0,"hash")}
        };
        foreach(var changes in invalid)
        {
            bool refused=false;try{AutomatedDeleteWake.ValidateReview(changes);}catch(InvalidDataException){refused=true;}
            if(!refused)throw new Exception("Automation authorization guard failed.");
        }
        Console.WriteLine("PASS: authorized deletion accepted; six unauthorized change sets refused. No device contacted.");return;
    }
    if(args.Length==3 && args[0]=="--automated-delete-wake")
    {
        await AutomatedDeleteWake.RunAsync(args[1],args[2]);return;
    }
    if(args.Length!=3 || (args[0]!="--plan" && args[0]!="--execute-reviewed" && args[0]!="--diagnose-recovery" && args[0]!="--recover-reviewed"))throw new ArgumentException("Expected --plan, --diagnose-recovery, --recover-reviewed or --execute-reviewed and two paths.");
    if(args[0]=="--recover-reviewed")
    {
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))!="3AA0FF7325E6E1FF22E29AD2D5B020AB31F2754A6310C4F931CE5EDDCBFC5C86")throw new InvalidDataException("Recovery receipt changed.");
        using var review=JsonDocument.Parse(File.ReadAllText(args[1]));var receipt=review.RootElement;
        var settings=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(receipt.GetProperty("ConfigurationPath").GetString()!),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Configuration missing");
        if(settings.Zidoo.ExpectedModel!=receipt.GetProperty("ExpectedModel").GetString())throw new InvalidDataException("Configured model differs from reviewed recovery.");
        using var handler=new System.Net.Http.HttpClientHandler{AllowAutoRedirect=false,UseProxy=false};
        using var client=new System.Net.Http.HttpClient(handler){BaseAddress=new UriBuilder("http",settings.Zidoo.IPAddress,settings.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        var device=new NativeZidooPlaylistDevice(client,settings);await device.VerifyIdentityAsync();await device.PreflightDirectoriesAsync();
        Console.WriteLine("RECOVERY ONLY: restore original Coolio with 18 tracks; no playlist deletion and no new synchronization.");
        await new NativeProductionRecovery(device).RecoverOriginalAsync(settings.NAS.PlaylistFolderUNC,receipt.GetProperty("Transaction").GetString()!,receipt.GetProperty("IntentSHA256").GetString()!,receipt.GetProperty("OriginalSHA256").GetString()!);
        Console.WriteLine("PASS: original Coolio 18-track state verified; other playlist identities/counts unchanged; recovery gate cleared. Receipt: "+receipt.GetProperty("Transaction").GetString()+"\\recovered-original.json");return;
    }
    if(args[0]=="--diagnose-recovery")
    {
        var settings=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(args[1]),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Configuration missing");
        string diagnostics=Path.Combine(Path.GetFullPath(args[2]),"NativeRecoveryDiagnosis-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(diagnostics);
        using var handler=new System.Net.Http.HttpClientHandler{AllowAutoRedirect=false,UseProxy=false};
        using var client=new System.Net.Http.HttpClient(handler){BaseAddress=new UriBuilder("http",settings.Zidoo.IPAddress,settings.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        var device=new NativeZidooPlaylistDevice(client,settings);await device.VerifyIdentityAsync();
        var inventory=await device.InventoryAsync();
        await File.WriteAllTextAsync(Path.Combine(diagnostics,"inventory.json"),JsonSerializer.Serialize(inventory,new JsonSerializerOptions{WriteIndented=true}));
        var directories=await device.DiagnoseDirectoriesAsync();
        await File.WriteAllTextAsync(Path.Combine(diagnostics,"directories.json"),JsonSerializer.Serialize(directories,new JsonSerializerOptions{WriteIndented=true}));
        string target=Path.Combine(settings.NAS.PlaylistFolderUNC,"Coolio.m3u");
        var coolio=inventory.SingleOrDefault(x=>x.Name=="Coolio");
        var diagnosisSummary=new{Scope="Read only: model, inventory, directory listings and Coolio file fingerprint; no keys or playlist writes",DevicePlaylists=inventory.Count,CoolioPresent=coolio is not null,CoolioTracks=coolio?.Count,NasCoolioSHA256=File.Exists(target)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))):null,PendingRecovery=File.Exists(Path.Combine(settings.NAS.PlaylistFolderUNC,".mmzs-production-pending.json")),Results=diagnostics};
        await File.WriteAllTextAsync(Path.Combine(diagnostics,"summary.json"),JsonSerializer.Serialize(diagnosisSummary,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(diagnosisSummary,new JsonSerializerOptions{WriteIndented=true}));return;
    }
    if(args[0]=="--execute-reviewed")
    {
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))!="42A9CA0AD9FD3C0166745802672EB244510300887E483977E41343E5687C81A4")
            throw new InvalidDataException("Reviewed authorization receipt differs from the explicitly approved live plan.");
        using var review=JsonDocument.Parse(File.ReadAllText(args[1]));
        var receipt=review.RootElement;
        string configPath=receipt.GetProperty("ConfigurationPath").GetString()!;
        string sourcePlan=receipt.GetProperty("SourcePlan").GetString()!;
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePlan)))!=receipt.GetProperty("SourcePlanSHA256").GetString())
            throw new InvalidDataException("Original reviewed plan changed.");
        string inventoryPath=receipt.GetProperty("InventoryPath").GetString()!;
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inventoryPath)))!=receipt.GetProperty("InventorySHA256").GetString())
            throw new InvalidDataException("Reviewed device baseline changed.");
        var liveConfig=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Configuration missing");
        if(liveConfig.Zidoo.ExpectedModel!=receipt.GetProperty("ExpectedModel").GetString())throw new InvalidDataException("Configured model changed since review.");
        liveConfig.Application.ReportRoot=Path.GetFullPath(args[2]);
        var changes=JsonSerializer.Deserialize<NativeProductionSyncService.ReviewedChange[]>(receipt.GetProperty("AllowedChanges").GetRawText())!;
        var inventory=JsonSerializer.Deserialize<ZidooPlaylistRecord[]>(File.ReadAllText(inventoryPath))!;
        Console.WriteLine("LIVE NATIVE SYNCHRONIZATION: approved A New Test create (2 tracks) and Coolio update (18 to 2 tracks). Do not operate the Zidoo remote during the test.");
        var sync=new NativeProductionSyncService();sync.Output+=Console.WriteLine;
        using var cancellation=new CancellationTokenSource();
        ConsoleCancelEventHandler cancel=(_,e)=>{e.Cancel=true;cancellation.Cancel();Console.WriteLine("Cancellation requested; let the program finish recovery and power restoration.");};
        Console.CancelKeyPress+=cancel;
        try{Environment.ExitCode=await sync.RunAsync(liveConfig,cancellation.Token,changes,inventory);}
        finally{Console.CancelKeyPress-=cancel;}
        Console.WriteLine("Native synchronization completed. Inspect run evidence before calling this live test accepted.");
        return;
    }
    var config=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(args[1]),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Missing configuration");
    ConfigurationService.Validate(config);
    string folder=Path.Combine(Path.GetFullPath(args[2]),"NativeProductionPlan-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(folder);
    // Exporter diagnostics are confined to this results folder; authoritative M3Us are read only.
    config.Application.ReportRoot=folder;
    var exporter=new MediaMonkeyPlaylistExporter();
    var validation=await exporter.ValidateAsync(config);
    var plan=await new MediaMonkeyExportTransactionPlanner().PrepareAsync(config);
    var fingerprints=plan.Items.Select(item=>{
        string path=Path.Combine(config.NAS.PlaylistFolderUNC,item.FileName);
        return new{item.FileName,Action=item.Action.ToString(),OriginalSHA256=File.Exists(path)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))):null};
    }).ToArray();
    await File.WriteAllTextAsync(Path.Combine(folder,"plan.json"),JsonSerializer.Serialize(new{Status="Prepared; no mutation authorized by this plan",Plan=plan,Files=fingerprints,PlaylistMap=validation.PlaylistMap},new JsonSerializerOptions{WriteIndented=true}));
    var summary=new{Status="PASS",Scope="Read-only MediaMonkey validation and NAS comparison; exporter reports only; no authoritative M3U writes, no Zidoo calls",validation.PlaylistCount,validation.TrackCount,plan.CreateCount,plan.UpdateCount,plan.UnchangedCount,plan.PendingZidooDeleteCount,Results=folder};
    await File.WriteAllTextAsync(Path.Combine(folder,"summary.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine(JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception error){Console.Error.WriteLine("FAIL: "+error);Environment.ExitCode=1;}
