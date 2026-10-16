using MediaMonkeyZidooSync.Services;

try
{
if(args.Length>0 && args[0]=="--probe") { await LiveProbe.Run(args); return; }
int checks = 0;
void Assert(bool result) { checks++; if (!result) throw new Exception("Check failed: " + checks); }
void Reject(string json)
{
    try { ZidooPlaylistProtocol.Parse(json); }
    catch (InvalidDataException) { checks++; return; }
    throw new Exception("Accepted unsafe inventory: " + json);
}
var rows = ZidooPlaylistProtocol.Parse("[{\"obj\":{\"id\":12,\"name\":\"Jazz\"},\"count\":3},{\"obj\":{\"id\":\"14\",\"name\":\"Rock\"},\"count\":\"0\"}]");
Assert(rows.Count == 2 && rows[0].Id == "12" && rows[0].Count == 3 && rows[1].GridIndex == 1);
Assert(ZidooPlaylistProtocol.Parse("[]").Count == 0);
Reject("{}");
Reject("[{\"id\":1,\"name\":\"Jazz\"}]");
Reject("[{\"obj\":{\"id\":1,\"name\":\"Jazz\"},\"count\":-1}]");
Reject("[{\"obj\":{\"id\":1,\"name\":\"Jazz\"},\"count\":1},{\"obj\":{\"id\":1,\"name\":\"Rock\"},\"count\":1}]");
Reject("[{\"obj\":{\"id\":1,\"name\":\"Jazz\"},\"count\":1},{\"obj\":{\"id\":2,\"name\":\"Jazz\"},\"count\":1}]");
Reject("[{\"obj\":{\"id\":1,\"name\":\"Jazz\"}}]");
Reject("[null]");
Reject("[" + string.Join(",", Enumerable.Range(0,500).Select(i => "{\"obj\":{\"id\":"+i+",\"name\":\"P"+i+"\"},\"count\":1}")) + "]");
Console.WriteLine($"PASS: {checks} native device inventory assertions; no device contacted.");
await DeviceChecks.Run();
await TransactionChecks.Run();
await RecoveryChecks.Run();
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: " + error);
    Environment.ExitCode = 1;
}

static class RecoveryChecks
{
    public static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"MMZS-Recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string journal=Path.Combine(root,"evidence");Directory.CreateDirectory(journal);
        string target=Path.Combine(root,"Coolio.m3u");
        byte[] old=System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n"+string.Join("\n",Enumerable.Range(0,18).Select(i=>"/Music/"+i+".flac"))+"\n");
        File.WriteAllBytes(target,old);File.WriteAllBytes(Path.Combine(journal,"original.m3u"),old);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(old));
        var original=new ZidooPlaylistRecord("175","Coolio",18,0);var other=new ZidooPlaylistRecord("20","Rock",3,1);
        string intent=Path.Combine(journal,"intent.json");File.WriteAllText(intent,System.Text.Json.JsonSerializer.Serialize(new{Target=target,ExpectedOldSHA256=hash,OriginalDevice=original,OriginalInventory=new[]{original,other}}));
        string intentHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(intent)));
        string pending=Path.Combine(root,".mmzs-production-pending.json");
        void Marker()=>File.WriteAllText(pending,System.Text.Json.JsonSerializer.Serialize(new{Journal=journal,Target=target}));
        Marker();var fake=new RecoveryDevice(other);var recovery=new NativeProductionRecovery(fake);
        await recovery.RecoverOriginalAsync(root,journal,intentHash,hash);
        if(fake.Imports!=1||fake.Rows.Single(x=>x.Name=="Coolio").Count!=18||File.Exists(pending))throw new Exception("Original recovery did not verify/release gate.");
        await recovery.RecoverOriginalAsync(root,journal,intentHash,hash);
        if(fake.Imports!=1)throw new Exception("Repeated recovery imported a duplicate.");
        File.AppendAllText(target,"/Music/external.flac\n");
        try{await recovery.RecoverOriginalAsync(root,journal,intentHash,hash);throw new Exception("External file edit accepted.");}catch(IOException){}
        File.WriteAllBytes(target,old);Marker();fake.Rows=new(){other with{Count=4}};
        try{await recovery.RecoverOriginalAsync(root,journal,intentHash,hash);throw new Exception("Collateral change accepted.");}catch(InvalidDataException){if(fake.Imports!=1||!File.Exists(pending))throw;}
        Console.WriteLine("PASS: 4 original-state recovery scenarios; simulated device and disposable files only.");
    }
    private sealed class RecoveryDevice:IProductionPlaylistDevice
    {
        public List<ZidooPlaylistRecord> Rows;public int Imports;
        public RecoveryDevice(ZidooPlaylistRecord other)=>Rows=new(){other};
        public Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<ZidooPlaylistRecord>>(Rows.ToArray());
        public Task DeleteExactAsync(ZidooPlaylistRecord identity,CancellationToken ct)=>throw new Exception("Recovery must not delete playlists.");
        public Task ImportAsync(string file,int count,CancellationToken ct){Imports++;Rows.Add(new("NEW","Coolio",count,Rows.Count));return Task.CompletedTask;}
    }
}

static class LiveProbe
{
    public static async Task Run(string[] args)
    {
        if(args.Length!=3)throw new ArgumentException("Usage: --probe CONFIG_PATH RESULTS_ROOT");
        var config=System.Text.Json.JsonSerializer.Deserialize<MediaMonkeyZidooSync.Models.AppConfig>(File.ReadAllText(args[1]),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new Exception("Configuration missing");
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false};
        using var http=new HttpClient(handler){BaseAddress=new UriBuilder("http",config.Zidoo.IPAddress,config.Zidoo.ApiPort).Uri,Timeout=TimeSpan.FromSeconds(15)};
        var device=new NativeZidooPlaylistDevice(http,config);
        await device.VerifyIdentityAsync();
        var inventory=await device.InventoryAsync();
        string folder=Path.Combine(args[2],"NativeReadOnlyProbe-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder,"inventory.json"),System.Text.Json.JsonSerializer.Serialize(inventory,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        await File.WriteAllTextAsync(Path.Combine(folder,"summary.json"),System.Text.Json.JsonSerializer.Serialize(new {Status="PASS",Scope="Read-only getModel and getSongLists; no remote keys, app launch, power operations or playlist/file mutation",Model=config.Zidoo.ExpectedModel,Playlists=inventory.Count,Time=DateTimeOffset.Now}));
        Console.WriteLine("PASS: configured device model and "+inventory.Count+" playlist identities parsed. Results: "+folder);
    }
}

static class TransactionChecks
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "MMZS-Native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fake = new MemoryDevice();
        var transaction = new NativeProductionTransaction(fake);
        byte[] data = System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n/Music/test.flac\n");
        await transaction.ApplyAsync(root, Path.Combine(root,"Evidence"), new("Jazz.m3u", data, 1, null), default);
        if(!File.ReadAllBytes(Path.Combine(root,"Jazz.m3u")).SequenceEqual(data) || fake.Rows.Single().Count != 1 || File.Exists(Path.Combine(root,".mmzs-production-pending.json"))) throw new Exception("Create did not commit.");
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        byte[] updated=System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n/Music/test.flac\n/Music/second.flac\n");
        fake.FailImportForCount=2;
        try{await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Jazz.m3u",updated,2,hash),default);throw new Exception("Failed update was accepted.");}
        catch(NativeTransactionRolledBackException){if(!File.ReadAllBytes(Path.Combine(root,"Jazz.m3u")).SequenceEqual(data)||fake.Rows.Single().Count!=1)throw new Exception("Update rollback failed to restore original file/device.");}
        fake.FailImportForCount=null;
        await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Jazz.m3u",updated,2,hash),default);
        if(!File.ReadAllBytes(Path.Combine(root,"Jazz.m3u")).SequenceEqual(updated)||fake.Rows.Single().Count!=2)throw new Exception("Update did not commit.");
        hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(updated));
        try{await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Jazz.m3u",data,2,hash),default);throw new Exception("Candidate count mismatch accepted.");}
        catch(InvalidDataException){if(fake.Rows.Single().Count!=2)throw;}
        try{ await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Jazz.m3u",data,1,"WRONG"),default);throw new Exception("Concurrent edit accepted."); }
        catch(IOException){if(fake.Rows.Count!=1)throw;}
        await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Jazz.m3u",null,0,hash),default);
        if(File.Exists(Path.Combine(root,"Jazz.m3u")) || fake.Rows.Count!=0)throw new Exception("Delete did not commit.");
        var retained = new ZidooPlaylistRecord("existing", "AlreadyPresent", 1, 0);
        fake.Rows.Add(retained);int beforeOperations=fake.Operations;
        await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("AlreadyPresent.m3u",data,1,null),default);
        if(fake.Operations!=beforeOperations || fake.Rows.Single()!=retained || !File.ReadAllBytes(Path.Combine(root,"AlreadyPresent.m3u")).SequenceEqual(data))
            throw new Exception("Matching existing creation was destructively replaced.");
        fake.Rows.Clear();
        fake.FailImport=true;
        try{await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Jazz.m3u",data,1,null),default);throw new Exception("Device failure accepted.");}
        catch(NativeTransactionRolledBackException){if(File.Exists(Path.Combine(root,".mmzs-production-pending.json")) || File.Exists(Path.Combine(root,"Jazz.m3u")))throw new Exception("Rollback did not restore original absence.");}
        fake.ResetFailure();fake.FailInventoryAfterImport=true;
        try{await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Jazz.m3u",data,1,null),default);throw new Exception("Uncertain device state accepted.");}
        catch(IOException){if(!File.Exists(Path.Combine(root,".mmzs-production-pending.json")))throw new Exception("Recovery gate absent.");}
        fake.FailInventoryAfterImport=false;
        int operations=fake.Operations;
        string currentHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        try{await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Jazz.m3u",data,1,currentHash),default);throw new Exception("Unresolved recovery gate ignored.");}
        catch(IOException){if(fake.Operations!=operations)throw new Exception("Device mutated with pending recovery.");}
        foreach(bool externalEdit in new[]{false,true})
        {
            string caseRoot=Path.Combine(root,"Collateral-"+externalEdit);Directory.CreateDirectory(caseRoot);
            File.WriteAllBytes(Path.Combine(caseRoot,"Jazz.m3u"),data);
            File.WriteAllBytes(Path.Combine(caseRoot,"Rock.m3u"),updated);
            var collateral=new MemoryDevice();
            var jazz=new ZidooPlaylistRecord("jazz","Jazz",1,0);var rock=new ZidooPlaylistRecord("rock","Rock",2,1);
            collateral.Rows.AddRange(new[]{jazz,rock});
            collateral.DeleteFailure=()=>{collateral.Rows.Remove(rock);if(externalEdit)File.AppendAllText(Path.Combine(caseRoot,"Rock.m3u"),"/external.flac\n");throw new IOException("Simulated wrong-target deletion");};
            try{await new NativeProductionTransaction(collateral).ApplyAsync(caseRoot,Path.Combine(caseRoot,"Evidence"),new("Jazz.m3u",updated,2,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))),default);throw new Exception("Collateral deletion accepted");}
            catch(NativeTransactionRolledBackException) when(!externalEdit)
            {
                if(collateral.Rows.Single(x=>x.Name=="Rock").Count!=2 || collateral.Rows.Single(x=>x.Name=="Jazz")!=jazz || File.Exists(Path.Combine(caseRoot,".mmzs-production-pending.json")))throw new Exception("Collateral restoration did not verify original state");
            }
            catch(IOException) when(externalEdit)
            {
                if(!File.Exists(Path.Combine(caseRoot,".mmzs-production-pending.json")) || collateral.Rows.Any(x=>x.Name=="Rock"))throw new Exception("External edit did not block repair");
            }
        }
        Console.WriteLine("PASS: 12 native file/device transaction scenarios; disposable local folder, simulated device only. Evidence: "+root);
    }
    private sealed class MemoryDevice:IProductionPlaylistDevice
    {
        public List<ZidooPlaylistRecord> Rows=new();public bool FailImport,FailInventoryAfterImport;public int? FailImportForCount;private bool importFailed;public int Operations;public Action? DeleteFailure;
        public void ResetFailure()=>importFailed=false;
        public Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct){if(FailInventoryAfterImport && importFailed)throw new IOException("Inventory unavailable");return Task.FromResult<IReadOnlyList<ZidooPlaylistRecord>>(Rows.ToArray());}
        public Task DeleteExactAsync(ZidooPlaylistRecord row,CancellationToken ct){Operations++;if(DeleteFailure is not null){var failure=DeleteFailure;DeleteFailure=null;failure();}Rows.Remove(row);return Task.CompletedTask;}
        public Task ImportAsync(string filename,int count,CancellationToken ct){Operations++;if(FailImport||FailImportForCount==count){importFailed=true;throw new IOException("Simulated import failure");}Rows.Add(new(Guid.NewGuid().ToString(),Path.GetFileNameWithoutExtension(filename),count,Rows.Count));return Task.CompletedTask;}
    }
}

static class DeviceChecks
{
    public static async Task Run()
    {
        var config = new MediaMonkeyZidooSync.Models.AppConfig();
        config.Zidoo.ExpectedModel = "TEST"; config.Zidoo.PlaylistGridColumns = 4;
        config.NAS.Host = "offline.invalid"; config.NAS.ShareName = "Test";
        config.NAS.MusicFolderName = "Music"; config.NAS.PlaylistFolderName = "Lists";
        using var transport = new Replay();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("http://offline.invalid:9529") };
        var device = new NativeZidooPlaylistDevice(http, config, (_, ct) => Task.CompletedTask);
        await device.DeleteExactAsync(new("1", "Jazz", 2, 0));
        if (!transport.Deleted || transport.Keys.Count(x => x == "Key.Select") != 1)
            throw new Exception("Delete sequence was not exercised.");
        transport.Reset(); transport.WrongModel = true;
        try { await device.DeleteExactAsync(new("1", "Jazz", 2, 0)); throw new Exception("Wrong device accepted."); }
        catch (InvalidDataException) { if (transport.Keys.Count != 0) throw new Exception("Sent keys to wrong model."); }
        transport.Reset(); transport.ChangeInventory = true;
        try { await device.DeleteExactAsync(new("1", "Jazz", 2, 0)); throw new Exception("Changed inventory accepted."); }
        catch (InvalidDataException) { if (transport.Keys.Contains("Key.Select")) throw new Exception("Deleted after inventory changed."); }
        transport.Reset(); transport.Collateral = true;
        try { await device.DeleteExactAsync(new("1", "Jazz", 2, 0)); throw new Exception("Collateral change accepted."); }
        catch (InvalidDataException) { if (!transport.Deleted) throw; }
        transport.Reset(); transport.ImportMode = true;
        await device.ImportAsync("Jazz.m3u", 2);
        if (!transport.Imported) throw new Exception("Import sequence not exercised.");
        transport.Reset(); transport.ImportMode = true; transport.BadImportCount = true;
        try { await device.ImportAsync("Jazz.m3u", 2); throw new Exception("Incorrect import count accepted."); }
        catch (InvalidDataException) { if (!transport.Imported) throw; }
        transport.Reset();transport.ImportMode=true;transport.DuplicateListing=true;
        try{await device.ImportAsync("Jazz.m3u",2);throw new Exception("Ambiguous import directory accepted.");}
        catch(InvalidDataException){if(transport.Keys.Count!=0)throw new Exception("Navigation began before directory validation.");}
        transport.Reset();transport.ImportMode=true;transport.UnrelatedDuplicates=true;
        await device.ImportAsync("Jazz.m3u",2);
        if(!transport.Imported)throw new Exception("Unrelated duplicate directory names blocked a unique import target.");
        Console.WriteLine("PASS: 8 native device transport scenarios; fake HTTP handler only, no network or device access.");
    }

    private sealed class Replay : HttpMessageHandler
    {
        public bool Deleted, WrongModel, ChangeInventory, Collateral, ImportMode, Imported, BadImportCount,DuplicateListing,UnrelatedDuplicates;
        public List<string> Keys = new();
        private int inventories;
        public void Reset() { Deleted=WrongModel=ChangeInventory=Collateral=ImportMode=Imported=BadImportCount=DuplicateListing=UnrelatedDuplicates=false; Keys.Clear(); inventories=0; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            string json = "{\"status\":200}";
            if (uri.AbsolutePath.EndsWith("getModel")) json = "{\"status\":200,\"model\":\""+(WrongModel?"OTHER":"TEST")+"\"}";
            else if (uri.AbsolutePath.EndsWith("getSongLists"))
            {
                inventories++;
                var other="{\"obj\":{\"id\":2,\"name\":\"Rock\"},\"count\":3}";
                var target="{\"obj\":{\"id\":1,\"name\":\"Jazz\"},\"count\":"+(BadImportCount?4:2)+"}";
                json = ImportMode ? (Imported?"["+other+","+target+"]":"["+other+"]") :
                    Deleted ? (Collateral?"[]":"["+other+"]") :
                    ChangeInventory && inventories>1?"["+other+","+target+"]":"["+target+","+other+"]";
            }
            else if (uri.AbsolutePath.EndsWith("sendkey"))
            {
                string key=Uri.UnescapeDataString(uri.Query.Split('=')[1]); Keys.Add(key);
                if(key=="Key.Select") Deleted=true;
                if(ImportMode && Keys.Count>=2 && Keys[^2]=="Key.Right" && key=="Key.Ok") Imported=true;
            }
            else if (uri.AbsolutePath.EndsWith("getFileList"))
            {
                string path=Uri.UnescapeDataString(uri.Query.Split('&')[0][6..]);
                var names=path.EndsWith("/Lists")?new[]{"Jazz.m3u"}:path.EndsWith("/Music")?new[]{"Lists"}:new[]{"Music"};
                if(DuplicateListing && path.EndsWith("/Music"))names=new[]{"Lists","Lists"};
                if(UnrelatedDuplicates && path.EndsWith("/Music"))names=new[]{"Artist","Artist","Lists"};
                json="{\"status\":200,\"filelist\":["+string.Join(",",names.Select(n=>"{\"name\":\""+n+"\"}"))+"]}";
            }
            else if (!uri.AbsolutePath.EndsWith("openApp")) throw new Exception("Unexpected endpoint "+uri);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
}
