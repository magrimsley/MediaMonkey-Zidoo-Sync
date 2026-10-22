using System.Net;
using System.Security.Cryptography;
using System.Text;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

static class RC8Checks
{
    public static async Task Run()
    {
        foreach(int matches in new[]{0,1,2})
        {
            var config=new AppConfig();config.Zidoo.ExpectedModel="TEST";config.Zidoo.PlaylistGridColumns=5;
            config.NAS.Host="Home";config.NAS.ShareName="Grimsley";config.NAS.MusicFolderName="Music";config.NAS.PlaylistFolderName="A Zidoo";
            using var handler=new Folders(matches);using var http=new HttpClient(handler){BaseAddress=new Uri("http://fixture.invalid")};
            var device=new NativeZidooPlaylistDevice(http,config,(_,_)=>Task.CompletedTask,(_,_)=>Task.FromResult(new[]{IPAddress.Parse("192.0.2.1"),IPAddress.Parse("192.0.2.2")}));
            try { await device.PreflightDirectoriesAsync();if(matches!=1)throw new Exception("Unsafe host resolution accepted"); }
            catch(InvalidDataException) { if(matches==1)throw; }
            if(config.NAS.Host!="Home" || handler.Keys!=0)throw new Exception("Host resolution modified settings or navigated");
        }
        string root=Path.Combine(Path.GetTempPath(),"MMZS-RC8-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            byte[] original=Encoding.UTF8.GetBytes("#EXTM3U\r\n/Music/original.flac\r\n");
            byte[] candidate=Encoding.UTF8.GetBytes("#EXTM3U\r\n/Music/a.flac\r\n/Music/b.flac\r\n");
            string target=Path.Combine(root,"Hidden.m3u");File.WriteAllBytes(target,original);File.SetAttributes(target,FileAttributes.Hidden);
            var fake=new Device();fake.Rows.Add(new("old","Hidden",1,0));
            var transaction=new NativeProductionTransaction(fake);
            fake.FailTwo=true;
            try { await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Hidden.m3u",candidate,2,Hash(original)),default);throw new Exception("Failure accepted"); }
            catch(NativeTransactionRolledBackException) { }
            if(!File.ReadAllBytes(target).SequenceEqual(original)||fake.Rows.Single().Count!=1||File.Exists(Path.Combine(root,".mmzs-production-pending.json")))throw new Exception("Hidden file rollback failed");
            fake.FailTwo=false;
            await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Hidden.m3u",candidate,2,Hash(original)),default);
            if(!File.ReadAllBytes(target).SequenceEqual(candidate)||fake.Rows.Single().Count!=2)throw new Exception("Hidden file commit failed");
            // Frozen CREATE verifies an existing matching playlist without deleting/reimporting it.
            var retained=fake.Rows.Single();
            try { await transaction.ApplyAsync(root,Path.Combine(root,"Evidence"),new("Hidden.m3u",candidate,2,Hash(candidate),true),default);throw new Exception("Count-only physical CREATE accepted"); }
            catch(InvalidDataException) { }
            if(fake.Rows.Single()!=retained)throw new Exception("Refused CREATE replaced an existing playlist identity");
            File.SetAttributes(target,FileAttributes.Normal);
        }
        finally { Directory.Delete(root,true); }
        Console.WriteLine("PASS: RC8 hidden-target commit/rollback and unique, missing, ambiguous hostname folder fixtures; no device contacted.");
    }
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    sealed class Device:IProductionPlaylistDevice
    {
        public List<ZidooPlaylistRecord> Rows=new();public bool FailTwo;
        public Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<ZidooPlaylistRecord>>(Rows.ToArray());
        public Task DeleteExactAsync(ZidooPlaylistRecord row,CancellationToken ct){Rows.Remove(row);return Task.CompletedTask;}
        public Task ImportAsync(string file,int count,CancellationToken ct){if(FailTwo&&count==2)throw new IOException("fixture import failure");Rows.Add(new(Guid.NewGuid().ToString(),Path.GetFileNameWithoutExtension(file),count,0));return Task.CompletedTask;}
    }
    sealed class Folders(int matches):HttpMessageHandler
    {
        public int Keys;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(!request.RequestUri!.AbsolutePath.EndsWith("getFileList")){Keys++;throw new Exception("Unexpected device command");}
            string path=Uri.UnescapeDataString(request.RequestUri.Query);
            bool valid=path.Contains("192.0.2.1#")&&matches>=1 || path.Contains("192.0.2.2#")&&matches>=2;
            string[] names= !valid?[]:path.Contains("/Music/A Zidoo")?[]:path.Contains("/Music")?["A Zidoo"]:["Music"];
            string json=System.Text.Json.JsonSerializer.Serialize(new{status=200,filelist=names.Select(name=>new{name})});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
}
