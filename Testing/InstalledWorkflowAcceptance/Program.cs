using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MediaMonkeyZidooSync;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;
using Microsoft.Data.Sqlite;

/// <summary>Explicit opt-in hardware acceptance against an exact packaged assembly.
/// Uses a database copy and owned NAS mirror; never edits the user's database or authoritative M3Us.</summary>
internal static partial class Program
{
    private sealed record Fixture(string Root,string Nonce,string Name,int PlaylistId,string SourceDatabase,
        string SourceFolder,string ConfigRoot,AppConfig Config,string CandidateSHA256,ZidooPlaylistRecord[] Baseline);
    private static Fixture fixture=null!;
    private static Application app=null!;
    private static MainWindow window=null!;
    private static Exception? dispatcherFailure;
    private static readonly List<string> passed=new();
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Check(bool result,string name){if(!result)throw new InvalidDataException(name);passed.Add(name);Console.WriteLine("PASS: "+name);SaveProgress();}
    private static void SaveProgress()=>WriteEvidence(Path.Combine(fixture.Root,"checks.json"),passed);
    private static void Pump()
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    private static void Wait(Func<bool> condition,int seconds=420)
    {var deadline=DateTime.UtcNow.AddSeconds(seconds);while(!condition()){if(dispatcherFailure is not null)throw new InvalidOperationException("Acceptance dispatcher failed",dispatcherFailure);if(DateTime.UtcNow>deadline)throw new TimeoutException("Acceptance step did not complete.");Pump();}}
    private static T Await<T>(Task<T> task){Wait(()=>task.IsCompleted);return task.GetAwaiter().GetResult();}
    private static void Await(Task task){Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static IEnumerable<DependencyObject> Walk(DependencyObject parent)
    {yield return parent;for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(parent,i)))yield return child;}
    private static IReadOnlyList<LogEvent> Events()=>Field<List<LogEvent>>(window,"_events").ToArray();
    private static void Click(string name){var button=Field<Button>(window,name);Check(button.IsEnabled,name+" enabled at its required step");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private static void CloseNotifications(){foreach(var item in app.Windows.OfType<Window>().Where(item=>item!=window).ToArray())item.Close();}
    private static void ReviewAndRun(int expectedChanges,bool cancel=false,bool recovering=false)
    {
        Click("ValidateMediaMonkeyButton");Wait(()=>!Field<SynchronizationWorkflow>(window,"_workflow").Busy);CloseNotifications();
        Check(Field<TextBlock>(window,"StatusText").Text=="MediaMonkey validation passed","MediaMonkey validation passed");
        Click("VerifyNativeExporterButton");Wait(()=>!Field<SynchronizationWorkflow>(window,"_workflow").Busy);CloseNotifications();
        var review=Field<IReadOnlyList<NativeProductionSyncService.ReviewedChange>>(window,"_preparedReview");
        Check(review is not null && review.Count==expectedChanges && review.All(item=>item.FileName==fixture.Name+".m3u"),"Review contains only the owned test playlist");
        var accept=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
        accept.Tick+=(_,_)=>
        {
            var modal=app.Windows.OfType<Window>().SingleOrDefault(item=>item.Title=="Review Synchronization Changes");if(modal is null)return;
            var button=Walk(modal).OfType<Button>().Single(item=>item.Content?.ToString()=="Use Reviewed Plan");accept.Stop();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        accept.Start();try{Click("ReviewSynchronizationButton");}finally{accept.Stop();}
        DispatcherTimer? cancellation=null;DateTime? observed=null;
        if(cancel)
        {
            cancellation=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            cancellation.Tick+=(_,_)=>
            {
                if(Events().Any(item=>item.Message=="Update: "+fixture.Name+".m3u"))observed??=DateTime.UtcNow;
                if(observed is not null && DateTime.UtcNow-observed>TimeSpan.FromSeconds(1))
                {cancellation.Stop();Field<Button>(window,"RunButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
            };cancellation.Start();
        }
        try{Click("RunButton");Wait(()=>!Field<SynchronizationWorkflow>(window,"_workflow").Busy);}finally{cancellation?.Stop();}
        var events=Events();File.WriteAllText(Path.Combine(fixture.Root,"ui-run-"+DateTime.Now.ToString("HHmmssfff")+".json"),JsonSerializer.Serialize(events));
        string status=Field<TextBlock>(window,"StatusText").Text;
        Check(cancel?status=="Cancelled":recovering?status.StartsWith("Recovery completed",StringComparison.Ordinal):status=="Completed","Expected UI outcome: "+status);
        Check(events.Any(item=>item.Level==(cancel?LogLevel.Yellow:LogLevel.Green)) && !events.Any(item=>item.Level==LogLevel.Red),"Correct completion/cancellation color without red errors");
        Check(events.All(item=>!UserMessages.ForDisplay(item.Message).Contains("Exception:",StringComparison.Ordinal) && !UserMessages.ForDisplay(item.Message).Contains("\n   at ",StringComparison.Ordinal)),"Visible messages contain no stack traces");
        Check(!Field<Button>(window,"RunButton").IsEnabled && Field<Button>(window,"ValidateMediaMonkeyButton").IsEnabled,"Workflow resets to validation");
        CloseNotifications();
    }
    private static HttpClient Client(AppConfig config)=>new(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false})
        {BaseAddress=new UriBuilder("http",config.Zidoo.IPAddress,config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
    private static async Task<bool> Reachable(AppConfig config)
    {using var tcp=new TcpClient();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));try{await tcp.ConnectAsync(config.Zidoo.IPAddress,config.Zidoo.ApiPort,timeout.Token);return true;}catch(Exception e)when(e is SocketException or OperationCanceledException){return false;}}
    private static async Task On(AppConfig config)
    {
        using var http=Client(config);var device=new NativeZidooPlaylistDevice(http,config);
        if(!await Reachable(config))
        {
            await new NativeZidooConnectivityPowerService(http).SendWakeOnLanAsync(config.Zidoo.MacAddress,config.Zidoo.BroadcastAddress,config.Zidoo.WolPort);
            var deadline=DateTime.UtcNow.AddSeconds(config.Timing.ZidooReadinessTimeoutSeconds);
            while(true){try{await device.VerifyIdentityAsync();break;}catch(Exception e)when(e is HttpRequestException or TaskCanceledException){if(DateTime.UtcNow>=deadline)throw;await Task.Delay(3000);}}
            await Task.Delay(TimeSpan.FromSeconds(config.Timing.ColdBootUiStabilizationSeconds));
        }
        await device.VerifyIdentityAsync();
    }
    private static async Task Off(AppConfig config)
    {
        if(!await Reachable(config))return;using var http=Client(config);await new NativeZidooPlaylistDevice(http,config).VerifyIdentityAsync();
        using var reply=await http.GetAsync("/ZidooControlCenter/RemoteControl/sendkey?key=Key.PowerOn.Poweroff");reply.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());if(json.RootElement.GetProperty("status").GetInt32()!=200)throw new IOException("Power command was not acknowledged.");
        var deadline=DateTime.UtcNow.AddSeconds(45);while(await Reachable(config)){if(DateTime.UtcNow>deadline)throw new TimeoutException("Off state not verified.");await Task.Delay(2000);}
    }
    private static async Task<ZidooPlaylistRecord[]> Inventory(AppConfig config)
    {using var http=Client(config);var device=new NativeZidooPlaylistDevice(http,config);await device.VerifyIdentityAsync();return (await device.InventoryAsync(default)).ToArray();}
    private static void CheckInventory(int? targetCount)
    {
        var rows=Await(Inventory(fixture.Config));var target=rows.SingleOrDefault(item=>item.Name==fixture.Name);
        Check(targetCount is null?target is null:target?.Count==targetCount,"Owned device playlist count matches expected state");
        Check(rows.Where(item=>item.Name!=fixture.Name).Select(item=>(item.Id,item.Name,item.Count)).OrderBy(item=>item.Id,StringComparer.Ordinal)
            .SequenceEqual(fixture.Baseline.Select(item=>(item.Id,item.Name,item.Count)).OrderBy(item=>item.Id,StringComparer.Ordinal)),"Other device playlist identities and counts unchanged");
    }
    private static SqliteConnection CopyDatabase(string source,string destination)
    {
        using var original=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Mode=SqliteOpenMode.ReadOnly}.ToString());original.Open();
        var copy=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destination,Mode=SqliteOpenMode.ReadWriteCreate}.ToString());copy.Open();original.BackupDatabase(copy);copy.CreateCollation("IUNICODE",(left,right)=>StringComparer.OrdinalIgnoreCase.Compare(left,right));return copy;
    }
    private static void SetTracks(int count)
    {
        if(Path.GetFullPath(fixture.Config.MediaMonkey.DatabasePath)==Path.GetFullPath(fixture.SourceDatabase) || !Path.GetFullPath(fixture.Config.MediaMonkey.DatabasePath).StartsWith(fixture.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only the disposable database may be changed.");
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=fixture.Config.MediaMonkey.DatabasePath,Mode=SqliteOpenMode.ReadWrite}.ToString());db.Open();db.CreateCollation("IUNICODE",(left,right)=>StringComparer.OrdinalIgnoreCase.Compare(left,right));
        using var transaction=db.BeginTransaction();using var remove=db.CreateCommand();remove.Transaction=transaction;
        remove.CommandText="DELETE FROM PlaylistSongs WHERE IDPlaylist=$id";remove.Parameters.AddWithValue("$id",fixture.PlaylistId);remove.ExecuteNonQuery();
        if(count<0){using var delete=db.CreateCommand();delete.Transaction=transaction;delete.CommandText="DELETE FROM Playlists WHERE IDPlaylist=$id";delete.Parameters.AddWithValue("$id",fixture.PlaylistId);delete.ExecuteNonQuery();}
        else
        {
            var songs=JsonSerializer.Deserialize<long[]>(File.ReadAllText(Path.Combine(fixture.Root,"test-song-ids.json")))!;
            for(int i=0;i<count;i++){using var insert=db.CreateCommand();insert.Transaction=transaction;insert.CommandText="INSERT INTO PlaylistSongs (IDPlaylist,IDSong,SongOrder) VALUES ($playlist,$song,$position)";insert.Parameters.AddWithValue("$playlist",fixture.PlaylistId);insert.Parameters.AddWithValue("$song",songs[i]);insert.Parameters.AddWithValue("$position",i);insert.ExecuteNonQuery();}
        }
        transaction.Commit();
    }
    private static Fixture Prepare(string sourceConfig,string results,string expectedHash,bool deferHootie=false)
    {
        if(Hash(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))!=expectedHash)throw new InvalidDataException("Loaded application differs from reviewed candidate.");
        var config=JsonSerializer.Deserialize<AppConfig>(File.ReadAllBytes(sourceConfig),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;ConfigurationService.Validate(config);
        string sourceFolder=config.NAS.PlaylistFolderUNC;string sourceDatabase=Environment.ExpandEnvironmentVariables(config.MediaMonkey.DatabasePath);
        string nonce=Guid.NewGuid().ToString("N")[..12];string root=Path.GetFullPath(Path.Combine(results,"LiveAcceptance-Alpha60.18.6-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+nonce));Directory.CreateDirectory(root);
        string name="MMZS Acceptance "+nonce;string copy=Path.Combine(root,"MM5-copy.DB");
        using var db=CopyDatabase(sourceDatabase,copy);using var parent=db.CreateCommand();parent.CommandText="SELECT IDPlaylist FROM Playlists WHERE PlaylistName=$name";parent.Parameters.AddWithValue("$name",config.MediaMonkey.PlaylistParent);
        int parentId=Convert.ToInt32(parent.ExecuteScalar()??throw new InvalidDataException("Playlist parent missing"));
        if(deferHootie)
        {
            using var query=db.CreateCommand();query.CommandText="SELECT IDPlaylist FROM Playlists WHERE PlaylistName=$name AND ParentPlaylist=$parent";
            query.Parameters.AddWithValue("$name","Hootie & Smash Mouth");query.Parameters.AddWithValue("$parent",parentId);
            int deferredId=Convert.ToInt32(query.ExecuteScalar()??throw new InvalidDataException("Deferred source playlist not found in copied database."));
            using var children=db.CreateCommand();children.CommandText="SELECT COUNT(*) FROM Playlists WHERE ParentPlaylist=$id";children.Parameters.AddWithValue("$id",deferredId);
            if(Convert.ToInt32(children.ExecuteScalar())!=0)throw new InvalidDataException("Deferred playlist is not a leaf; fixture preparation refused.");
            using var removeEntries=db.CreateCommand();removeEntries.CommandText="DELETE FROM PlaylistSongs WHERE IDPlaylist=$id";removeEntries.Parameters.AddWithValue("$id",deferredId);removeEntries.ExecuteNonQuery();
            using var removePlaylist=db.CreateCommand();removePlaylist.CommandText="DELETE FROM Playlists WHERE IDPlaylist=$id";removePlaylist.Parameters.AddWithValue("$id",deferredId);removePlaylist.ExecuteNonQuery();
            WriteEvidence(Path.Combine(root,"deferred-managed-playlists.json"),new[]{"Hootie & Smash Mouth"});
        }
        using var insert=db.CreateCommand();insert.CommandText="INSERT INTO Playlists (PlaylistName,ParentPlaylist) VALUES ($name,$parent); SELECT last_insert_rowid();";insert.Parameters.AddWithValue("$name",name);insert.Parameters.AddWithValue("$parent",parentId);int id=Convert.ToInt32(insert.ExecuteScalar());
        using var songs=db.CreateCommand();songs.CommandText="SELECT ID,SongPath FROM Songs ORDER BY ID";using var reader=songs.ExecuteReader();var selected=new List<long>();
        while(reader.Read() && selected.Count<2){string path=reader.GetString(1);int marker=path.IndexOf(@"\Music\",StringComparison.OrdinalIgnoreCase);if(marker>=0 && File.Exists(Path.Combine(config.NAS.MusicRootUNC,path[(marker+7)..])))selected.Add(reader.GetInt64(0));}
        if(selected.Count!=2)throw new InvalidDataException("Two existing readable test tracks were not found.");reader.Close();File.WriteAllText(Path.Combine(root,"test-song-ids.json"),JsonSerializer.Serialize(selected));
        string ownFolder=Path.Combine(config.NAS.MusicRootUNC,"00-MMZS-Test-"+nonce);
        if(Directory.Exists(ownFolder) || string.Equals(ownFolder,sourceFolder,StringComparison.OrdinalIgnoreCase))throw new IOException("Test storage already exists.");
        Directory.CreateDirectory(ownFolder);File.WriteAllText(Path.Combine(ownFolder,".mmzs-acceptance-owner.json"),JsonSerializer.Serialize(new{Nonce=nonce,Root=root}));
        var baselineFiles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in Directory.EnumerateFiles(sourceFolder,"*.m3u")){string file=Path.GetFileName(path);baselineFiles.Add(file,Hash(File.ReadAllBytes(path)));if(!deferHootie||file!="Hootie & Smash Mouth.m3u")File.Copy(path,Path.Combine(ownFolder,file),false);}
        File.WriteAllText(Path.Combine(root,"source-m3u-hashes.json"),JsonSerializer.Serialize(baselineFiles));
        config.MediaMonkey.DatabasePath=copy;config.NAS.PlaylistFolderUNC=ownFolder;config.NAS.PlaylistFolderName=Path.GetFileName(ownFolder);config.Application.ReportRoot=Path.Combine(root,"Reports");Directory.CreateDirectory(config.Application.ReportRoot);
        string configRoot=Path.Combine(root,"Profile");var service=new ConfigurationService(configRoot);Directory.CreateDirectory(service.CurrentDirectory);File.WriteAllText(service.CurrentPath,JsonSerializer.Serialize(config));
        return new(root,nonce,name,id,sourceDatabase,sourceFolder,configRoot,config,expectedHash,Array.Empty<ZidooPlaylistRecord>());
    }
    private static async Task RecoverOnly(string recipe)
    {
        fixture=JsonSerializer.Deserialize<Fixture>(File.ReadAllBytes(recipe))??throw new InvalidDataException("Fixture missing.");
        string root=fixture.Config.NAS.PlaylistFolderUNC;
        if(fixture.Name!="MMZS Acceptance "+fixture.Nonce || !OwnedFolderName(root) ||
            Hash(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))!=fixture.CandidateSHA256 ||
            !File.ReadAllText(Path.Combine(root,".mmzs-acceptance-owner.json")).Contains(fixture.Nonce,StringComparison.Ordinal) ||
            string.Equals(root,fixture.SourceFolder,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Owned recovery fixture required.");
        if(!File.Exists(Path.Combine(root,".mmzs-production-pending.json")) && !File.Exists(NativeProductionRunState.Marker(root)))
            throw new InvalidDataException("No interrupted test remains; no synchronization was started.");
        var service=new NativeProductionSyncService();service.Output+=Console.WriteLine;
        await service.RunAsync(fixture.Config);
        if(!service.LastRunRecovered)throw new IOException("Interrupted test was not recovered.");
        string path=Path.Combine(fixture.Root,"post-reboot-recovery.json");
        using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);
        output.Write(JsonSerializer.SerializeToUtf8Bytes(new{Status="PASS",RecoveryOnly=true,service.LastRunRecovered,NewSynchronizationPerformed=false,Time=DateTimeOffset.Now}));output.Flush(true);
        Console.WriteLine("PASS: interrupted owned test recovered; no new synchronization performed.");
    }
    private static async Task CrashWorker(string recipe)
    {
        fixture=JsonSerializer.Deserialize<Fixture>(File.ReadAllBytes(recipe))!;
        string root=fixture.Config.NAS.PlaylistFolderUNC;
        if(!fixture.Name.Equals("MMZS Acceptance "+fixture.Nonce,StringComparison.Ordinal) || !OwnedFolderName(root) ||
            Hash(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))!=fixture.CandidateSHA256 ||
            !File.ReadAllText(Path.Combine(root,".mmzs-acceptance-owner.json")).Contains(fixture.Nonce,StringComparison.Ordinal))throw new InvalidDataException("Owned live crash fixture required.");
        using var fileLock=new FileStream(Path.Combine(root,".mmzs-production.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var http=Client(fixture.Config);var device=new NativeZidooPlaylistDevice(http,fixture.Config);await device.VerifyIdentityAsync();await device.PreflightDirectoriesAsync();
        var inventory=await device.InventoryAsync(default);var target=inventory.Single(item=>item.Name==fixture.Name);
        if(target.Count!=1 || !inventory.Where(item=>item.Name!=fixture.Name).Select(item=>(item.Id,item.Name,item.Count)).OrderBy(item=>item.Id,StringComparer.Ordinal)
            .SequenceEqual(fixture.Baseline.Select(item=>(item.Id,item.Name,item.Count)).OrderBy(item=>item.Id,StringComparer.Ordinal)))throw new InvalidDataException("Live crash baseline changed.");
        var prepared=await new MediaMonkeyPlaylistExporter().ValidateAsync(fixture.Config);var own=prepared.PreparedPlaylists.Single(item=>item.M3UFile==fixture.Name+".m3u");
        byte[] candidate=Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join(Environment.NewLine,own.Lines)+Environment.NewLine)).ToArray();
        string evidence=Path.Combine(fixture.Config.Application.ReportRoot,"NativeSync-crash-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
        new NativeProductionRunState(1,root,evidence,fixture.Config.Zidoo.IPAddress,fixture.Config.Zidoo.ApiPort,fixture.Config.Zidoo.ExpectedModel,fixture.Config.Zidoo.MacAddress,true).Save(root);
        await new NativeProductionTransaction(device,point=>{if(point!="DeviceDeleted")return;File.WriteAllText(Path.Combine(fixture.Root,"crash-paused"),point);Thread.Sleep(Timeout.Infinite);})
            .ApplyAsync(root,evidence,new(fixture.Name+".m3u",candidate,2,Hash(File.ReadAllBytes(Path.Combine(root,fixture.Name+".m3u")))),default);
        throw new Exception("Live interruption checkpoint was not reached.");
    }
    private static void KillOwnedWorker()
    {
        string recipe=Path.Combine(fixture.Root,"fixture.json");File.WriteAllText(recipe,JsonSerializer.Serialize(fixture));
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
        if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--live-crash-worker");start.ArgumentList.Add(recipe);using var child=Process.Start(start)!;
        var deadline=DateTime.UtcNow.AddSeconds(100);
        while(!File.Exists(Path.Combine(fixture.Root,"crash-paused")))
        {if(child.HasExited)throw new IOException(child.StandardError.ReadToEnd());if(DateTime.UtcNow>deadline){child.Kill(true);throw new TimeoutException("Live crash boundary not reached; recovery evidence retained.");}Pump();}
        child.Kill(true);child.WaitForExit();Check(true,"Owned live worker terminated after verified test-playlist deletion");
    }
    private static void VerifyAndClean()
    {
        var files=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllBytes(Path.Combine(fixture.Root,"source-m3u-hashes.json")))!;
        Check(files.Count==Directory.EnumerateFiles(fixture.SourceFolder,"*.m3u").Count() && files.All(item=>Hash(File.ReadAllBytes(Path.Combine(fixture.SourceFolder,item.Key)))==item.Value),"Original authoritative M3U files unchanged");
        string own=fixture.Config.NAS.PlaylistFolderUNC;
        var deferred=DeferredManagedNames();
        Check(files.All(item=>deferred.Contains(Path.GetFileNameWithoutExtension(item.Key))?!File.Exists(Path.Combine(own,item.Key)):Hash(File.ReadAllBytes(Path.Combine(own,item.Key)))==item.Value),"Isolated mirror baseline unchanged, explicit deferred omission preserved");
        if(!OwnedFolderName(own) || !File.ReadAllText(Path.Combine(own,".mmzs-acceptance-owner.json")).Contains(fixture.Nonce,StringComparison.Ordinal))throw new IOException("Cleanup ownership invalid");
        var allowed=new HashSet<string>(files.Keys,StringComparer.OrdinalIgnoreCase){".mmzs-acceptance-owner.json",".mmzs-production.lock"};
        if(Directory.EnumerateFiles(own).Any(path=>!allowed.Contains(Path.GetFileName(path))) || Directory.EnumerateDirectories(own).Any())throw new IOException("Unknown test-folder contents preserved.");
        foreach(string path in Directory.EnumerateFiles(own))File.Delete(path);Directory.Delete(own,false);Check(true,"Owned temporary NAS mirror removed; test evidence retained locally");
    }
    [STAThread] private static int Main(string[] args)
    {
        SQLitePCL.Batteries_V2.Init();
        if(args.Length==4 && args[0]=="--prepare-native-deferred-hootie"){try{PrepareDeferredAcceptance(args[1],args[2],args[3]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==4 && args[0]=="--native-prepared"){try{NativeAcceptance(args[1],Path.GetDirectoryName(args[2])!,args[3],args[2]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==5 && args[0]=="--diagnose-cold-navigation"){try{DiagnoseColdNavigation(args[1],args[2],args[3],args[4]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==4 && args[0]=="--diagnose-refresh"){try{DiagnoseRefresh(args[1],args[2],args[3]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==4 && args[0]=="--diagnose-ordering-cold"){try{DiagnoseColdOrdering(args[1],args[2],args[3]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==4 && args[0]=="--diagnose-ordering"){try{DiagnoseOrdering(args[1],args[2],args[3]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==4 && args[0]=="--diagnose-readiness"){try{DiagnoseReadiness(args[1],args[2],args[3]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==5 && args[0]=="--diagnose-navigation"){try{DiagnoseNavigation(args[1],args[2],args[3],args[4]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==2 && args[0]=="--cleanup-owned"){try{CleanupOwnedFixture(args[1]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==4 && args[0]=="--native"){try{NativeAcceptance(args[1],args[2],args[3]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==2 && args[0]=="--recover-only"){try{RecoverOnly(args[1]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length==2 && args[0]=="--live-crash-worker"){try{CrashWorker(args[1]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        if(args.Length!=3){Console.Error.WriteLine("Explicit opt-in live acceptance: CONFIG_PATH RESULTS_ROOT EXPECTED_CANDIDATE_DLL_SHA256");return 2;}
        bool? initial=null;bool success=false;
        try
        {
            fixture=Prepare(args[0],args[1],args[2]);Console.WriteLine("Live test evidence: "+fixture.Root);SetTracks(2);
#pragma warning disable WPF0001
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,ThemeMode=ThemeMode.System};
#pragma warning restore WPF0001
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            app.DispatcherUnhandledException+=(_,eventArgs)=>{dispatcherFailure=eventArgs.Exception;eventArgs.Handled=true;};
            initial=Await(Reachable(fixture.Config));
            File.WriteAllText(Path.Combine(fixture.Root,"fixture.json"),JsonSerializer.Serialize(fixture));
            string powerReceipt=Path.Combine(fixture.Root,"power-ownership.json");
            using(var stream=new FileStream(powerReceipt,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough))
            {stream.Write(JsonSerializer.SerializeToUtf8Bytes(new{InitiallyReachable=initial,WakePlanned=!initial.Value,fixture.Config.Zidoo.IPAddress,fixture.Config.Zidoo.ExpectedModel,Time=DateTimeOffset.Now}));stream.Flush(true);}
            Await(On(fixture.Config));fixture=fixture with{Baseline=Await(Inventory(fixture.Config))};
            Check(!fixture.Baseline.Any(item=>item.Name==fixture.Name),"Owned device test name is unused");
            var sourceSettings=JsonSerializer.Deserialize<AppConfig>(File.ReadAllBytes(args[0]),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;sourceSettings.Application.ReportRoot=Path.Combine(fixture.Root,"SourceValidation");
            var baseline=Await(new MediaMonkeyPlaylistExporter().ValidateAsync(sourceSettings));
            Check(baseline.PlaylistMap.All(item=>item.TrackCount==0 ? !fixture.Baseline.Any(row=>row.Name==Path.GetFileNameWithoutExtension(item.M3UFile)):
                fixture.Baseline.SingleOrDefault(row=>row.Name==Path.GetFileNameWithoutExtension(item.M3UFile))?.Count==item.TrackCount),"Existing managed device playlists already match; no unrelated repair is needed");
            window=new MainWindow(fixture.ConfigRoot);window.Show();Wait(()=>Field<AppConfig?>(window,"_config") is not null);CloseNotifications();
            Check(!Field<Button>(window,"RunButton").IsEnabled && !Field<Button>(window,"VerifyNativeExporterButton").IsEnabled,"Installed candidate begins with required validation gate");
            ReviewAndRun(1);CheckInventory(2);
            SetTracks(1);ReviewAndRun(1);CheckInventory(1);
            SetTracks(2);ReviewAndRun(1,cancel:true);CheckInventory(1);
            KillOwnedWorker();ReviewAndRun(0,recovering:true);CheckInventory(1);
            SetTracks(1);ReviewAndRun(0);Check(Events().Any(item=>item.Message.Contains("not contacted or awakened",StringComparison.Ordinal)),"No-change run avoids device contact and wake");
            SetTracks(2);Await(Off(fixture.Config));ReviewAndRun(1);
            Check(!Await(Reachable(fixture.Config)),"Application restored original off/unreachable state after waking");
            Check(Events().Any(item=>item.Message.Contains("Wake-on-LAN",StringComparison.Ordinal)),"Application exercised wake path");
            ReviewAndRun(0);Check(!Await(Reachable(fixture.Config)),"No-change run while off leaves device off");
            Await(On(fixture.Config));CheckInventory(2);SetTracks(-1);ReviewAndRun(1);CheckInventory(null);
            VerifyAndClean();success=true;
        }
        catch(Exception error)
        {Console.Error.WriteLine("Acceptance stopped; retain evidence and do not rerun blindly. "+error);if(fixture is not null)File.WriteAllText(Path.Combine(fixture.Root,"failure.txt"),error.ToString());success=false;}
        finally
        {
            bool restored=false;
            try
            {
                if(app is not null && fixture is not null && initial is not null)
                {if(initial.Value)Await(On(fixture.Config));else Await(Off(fixture.Config));restored=true;}
            }
            catch(Exception restoreError)
            {success=false;Console.Error.WriteLine("Power restoration was not verified: "+restoreError);if(fixture is not null)File.WriteAllText(Path.Combine(fixture.Root,"power-restoration-failure.txt"),restoreError.ToString());}
            if(fixture is not null)File.WriteAllText(Path.Combine(fixture.Root,"completed.json"),JsonSerializer.Serialize(new{Status=success?"PASS":"FAIL",Checks=passed.Count,Version="1.1-alpha.60.18.6",fixture.CandidateSHA256,SourceDatabaseReadOnly=true,IsolatedTestPlaylist=fixture.Name,InitialReachable=initial,OriginalPowerRestored=restored,Time=DateTimeOffset.Now}));
            if(app is not null){foreach(var item in app.Windows.OfType<Window>().ToArray())item.Close();app.Shutdown();}
        }
        return success?0:1;
    }
}
