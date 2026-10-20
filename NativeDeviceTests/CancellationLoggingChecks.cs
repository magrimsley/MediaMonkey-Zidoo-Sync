using System.Security.Cryptography;
using MediaMonkeyZidooSync.Services;

internal static class CancellationLoggingChecks
{
    public static async Task Run()
    {
        foreach(string mode in new[]{"user-cancel","timeout","failed-recovery"})
        {
            string root=Path.Combine(Path.GetTempPath(),"MMZS-CancellationLog-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            byte[] old=System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n/Music/old.flac\n"),candidate=System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n/Music/new.flac\n");
            File.WriteAllBytes(Path.Combine(root,"Target.m3u"),old);
            using var cancellation=new CancellationTokenSource();
            var device=new Device(cancellation,mode);
            bool rejected=false;
            try{await new NativeProductionTransaction(device).ApplyAsync(root,Path.Combine(root,"Evidence"),new("Target.m3u",candidate,1,Convert.ToHexString(SHA256.HashData(old))),cancellation.Token);}
            catch(NativeTransactionRolledBackException)when(mode!="failed-recovery"){rejected=true;}
            catch(IOException)when(mode=="failed-recovery"){rejected=true;}
            if(!rejected)throw new Exception("Expected unsuccessful transaction: "+mode);
            string journal=Directory.GetDirectories(Path.Combine(root,"Evidence"),"Transaction-*").Single();
            if(mode=="timeout")
            {
                if(!File.ReadAllText(Path.Combine(journal,"failure.json")).Contains("TaskCanceledException") || File.Exists(Path.Combine(journal,"cancelled.json")))throw new Exception("Timeout lost true failure diagnostics");
            }
            else
            {
                string receipt=File.ReadAllText(Path.Combine(journal,"cancelled.json"));
                if(!receipt.Contains("User cancelled") || receipt.Contains("Exception") || File.Exists(Path.Combine(journal,"failure.json")))throw new Exception("User cancellation became an error trace");
            }
            if(mode=="failed-recovery")
            {
                if(!File.ReadAllText(Path.Combine(journal,"recovery-failure.json")).Contains("Recovery inventory unavailable") || !File.Exists(Path.Combine(root,".mmzs-production-pending.json")))throw new Exception("Recovery failure diagnostics or safety gate lost");
            }
            else if(!File.Exists(Path.Combine(journal,"rolled-back.json")) || !File.ReadAllBytes(Path.Combine(root,"Target.m3u")).SequenceEqual(old) || device.Original.Id!="original-id" || File.Exists(Path.Combine(root,".mmzs-production-pending.json")))throw new Exception("Cancellation/timeout rollback changed");
            Console.WriteLine("PASS: cancellation log classification and recovery evidence: "+mode);
        }
    }
    private sealed class Device(CancellationTokenSource source,string mode):IProductionPlaylistDevice
    {
        public ZidooPlaylistRecord Original=new("original-id","Target",1,0);
        private int inventories;
        public Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct)
        {
            if(++inventories>1 && mode=="failed-recovery")throw new IOException("Recovery inventory unavailable");
            return Task.FromResult<IReadOnlyList<ZidooPlaylistRecord>>(new[]{Original});
        }
        public Task DeleteExactAsync(ZidooPlaylistRecord identity,CancellationToken ct)
        {
            if(mode!="timeout")source.Cancel();
            throw new TaskCanceledException(mode=="timeout"?"Transport timeout":"User cancellation");
        }
        public Task ImportAsync(string filename,int expectedCount,CancellationToken ct)=>throw new Exception("Unexpected import");
    }
}
