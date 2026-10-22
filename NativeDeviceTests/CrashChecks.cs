using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaMonkeyZidooSync.Services;

/// <summary>Real process termination, persistent fake device, disposable files; no HTTP or live endpoints.</summary>
internal static class CrashChecks
{
    private static readonly byte[] Original=Encoding.UTF8.GetBytes("#EXTM3U\n/Music/original.flac\n");
    private static readonly byte[] Candidate=Encoding.UTF8.GetBytes("#EXTM3U\n/Music/one.flac\n/Music/two.flac\n");
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    public static async Task Worker(string root,string operation,string phase)
    {
        string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(root).StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(root,".mmzs-crash-test")))
            throw new InvalidOperationException("Disposable crash-test fixture required.");
        var device=new FileDevice(root);
        var earlier=new NativeProductionTransaction(device);
        await earlier.ApplyAsync(root,Path.Combine(root,"NativeSync-fixture"),new("Earlier.m3u",Original,1,null),default);
        if(operation!="Create")
        {
            File.WriteAllBytes(Path.Combine(root,"Target.m3u"),Original);
            await device.ImportAsync("Target.m3u",1,default);
        }
        var transaction=new NativeProductionTransaction(device,point=>
        {
            if(point!=phase)return;
            File.WriteAllText(Path.Combine(root,"paused"),point);
            Thread.Sleep(Timeout.Infinite);
        });
        await transaction.ApplyAsync(root,Path.Combine(root,"NativeSync-fixture"),new("Target.m3u",
            operation=="Delete"?null:operation=="Empty"?Encoding.UTF8.GetBytes("#EXTM3U\n"):Candidate,
            operation is "Delete" or "Empty"?0:2,operation=="Create"?null:Hash(Original) /* Deferred: ,false,operation=="Repair" */),default);
        throw new Exception("Requested crash checkpoint was not reached.");
    }
    private static async Task<string> KilledFixture(string operation,string phase)
    {
        string root=Path.Combine(Path.GetTempPath(),"MMZS-Crash-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,".mmzs-crash-test"),"Disposable fake-device test");
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true};
        if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(CrashChecks).Assembly.Location);
        foreach(string argument in new[]{"--crash-worker",root,operation,phase})start.ArgumentList.Add(argument);
        using var child=Process.Start(start)??throw new IOException("Crash worker did not start.");
        var deadline=DateTime.UtcNow.AddSeconds(25);
        while(!File.Exists(Path.Combine(root,"paused")))
        {
            if(child.HasExited)throw new Exception("Crash worker exited: "+await child.StandardError.ReadToEndAsync());
            if(DateTime.UtcNow>deadline){child.Kill(true);throw new TimeoutException("Crash checkpoint not reached.");}
            await Task.Delay(30);
        }
        child.Kill(true);await child.WaitForExitAsync();return root;
    }
    // A process exit notification can precede release of a fixture handle by
    // Windows or another local observer. Keep exclusive ownership mandatory;
    // retry only sharing/lock violations, with a fixed deadline.
    private static async Task<FileStream> AcquireFixtureLock(string root,int timeoutMs=5000)
    {
        var timer=Stopwatch.StartNew();
        while(true)
        {
            try{return new FileStream(Path.Combine(root,".mmzs-production.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException error)when((error.HResult&0xffff) is 32 or 33 && timer.ElapsedMilliseconds<timeoutMs)
            {await Task.Delay(25);}
        }
    }
    public static async Task RecoverWorker(string root,string phase)
    {
        string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(root).StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(root,".mmzs-crash-test")))
            throw new InvalidOperationException("Disposable crash-test fixture required.");
        using var fileLock=await AcquireFixtureLock(root);
        await new NativeInterruptedRecovery(new FileDevice(root),point=>
        {if(point!=phase)return;File.WriteAllText(Path.Combine(root,"recovery-paused"),point);Thread.Sleep(Timeout.Infinite);}).RecoverUnderLockAsync(root,root);
        throw new Exception("Recovery checkpoint not reached.");
    }
    public static async Task Run()
    {
        int checks=0;
        string lockRoot=Path.Combine(Path.GetTempPath(),"MMZS-Crash-Lock-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lockRoot);
        string lockPath=Path.Combine(lockRoot,".mmzs-production.lock");
        using(var held=new FileStream(lockPath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
        {
            Task<FileStream> pending=AcquireFixtureLock(lockRoot);
            await Task.Delay(100);
            if(pending.IsCompleted)throw new Exception("Fixture lock acquisition bypassed a held exclusive lock");
            held.Dispose();
            using var acquired=await pending;
        }
        checks++;
        using(var held=new FileStream(lockPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            bool refused=false;
            try{using var unexpected=await AcquireFixtureLock(lockRoot,100);}
            catch(IOException error)when((error.HResult&0xffff) is 32 or 33){refused=true;}
            if(!refused)throw new Exception("Fixture lock deadline did not refuse a persistent owner");
        }
        checks++;
        foreach(string operation in new[]{"Create","Update","Delete","Empty"} /* Deferred: ,"Repair" */)
        foreach(string phase in new[]{"Prepared","FilePublished","DeviceDeleted","DeviceImported","Committed"})
        {
            string root=await KilledFixture(operation,phase);
            var device=new FileDevice(root);var earlier=device.Read().Single(row=>row.Name=="Earlier");
            using var fileLock=await AcquireFixtureLock(root);
            var recovery=new NativeInterruptedRecovery(device);
            string outcome=await recovery.RecoverUnderLockAsync(root,root);
            var rows=device.Read();var target=rows.SingleOrDefault(row=>row.Name=="Target");
            bool final=phase is "DeviceImported" or "Committed" || operation=="Empty" && phase=="DeviceDeleted";
            if(outcome!=(final?"Completed":"Restored") || rows.Single(row=>row.Name=="Earlier")!=earlier ||
                File.Exists(Path.Combine(root,".mmzs-production-pending.json")))throw new Exception("Incorrect crash reconciliation: "+operation+" / "+phase);
            if(final)
            {
                if(operation is "Delete" or "Empty" ? target is not null : target?.Count!=2)throw new Exception("Final device state not verified");
                if(operation=="Delete" ? File.Exists(Path.Combine(root,"Target.m3u")) :
                    !File.ReadAllBytes(Path.Combine(root,"Target.m3u")).SequenceEqual(operation=="Empty"?Encoding.UTF8.GetBytes("#EXTM3U\n"):Candidate))throw new Exception("Final file state incorrect");
            }
            else if(operation=="Create" ? target is not null || File.Exists(Path.Combine(root,"Target.m3u")) :
                target?.Count!=(1) || !File.ReadAllBytes(Path.Combine(root,"Target.m3u")).SequenceEqual(Original))throw new Exception("Original state not restored");
            checks++;
        }
        foreach(string tamper in new[]{"external-file","intent","candidate","other-device","legacy-marker"})
        {
            string root=await KilledFixture("Update","FilePublished");var device=new FileDevice(root);
            var pending=NativeInterruptedRecovery.ReadPending(root,root);
            switch(tamper)
            {
                case "external-file":File.AppendAllText(pending.Target,"/external.flac\n");break;
                case "intent":File.AppendAllText(Path.Combine(pending.Journal,"intent.json")," ");break;
                case "candidate":File.AppendAllText(Path.Combine(pending.Journal,"candidate.m3u"),"/external.flac\n");break;
                case "other-device":await device.ImportAsync("Unrelated.m3u",3,default);break;
                case "legacy-marker":File.WriteAllText(Path.Combine(root,".mmzs-production-pending.json"),JsonSerializer.Serialize(new{pending.Journal,pending.Target}));break;
            }
            string fileHash=Hash(File.ReadAllBytes(pending.Target));var before=device.Read();bool refused=false;
            try{await new NativeInterruptedRecovery(device).RecoverUnderLockAsync(root,root);}
            catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException){refused=true;}
            if(!refused || !File.Exists(Path.Combine(root,".mmzs-production-pending.json")) ||
                Hash(File.ReadAllBytes(pending.Target))!=fileHash || !device.Read().SequenceEqual(before))throw new Exception("Unsafe crash recovery accepted: "+tamper);
            checks++;
        }
        foreach(string phase in new[]{"FileRestored","DeviceRestored","ReceiptSaved"})
        {
            string root=await KilledFixture("Update","DeviceDeleted");
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardError=true,CreateNoWindow=true};
            if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(CrashChecks).Assembly.Location);
            foreach(string argument in new[]{"--recover-worker",root,phase})start.ArgumentList.Add(argument);
            using var child=Process.Start(start)!;var deadline=DateTime.UtcNow.AddSeconds(25);
            while(!File.Exists(Path.Combine(root,"recovery-paused")))
            {if(child.HasExited)throw new Exception(await child.StandardError.ReadToEndAsync());if(DateTime.UtcNow>deadline){child.Kill(true);throw new TimeoutException("Recovery checkpoint not reached");}await Task.Delay(30);}
            child.Kill(true);await child.WaitForExitAsync();
            var device=new FileDevice(root);
            using var fileLock=await AcquireFixtureLock(root);
            if(await new NativeInterruptedRecovery(device).RecoverUnderLockAsync(root,root)!="Restored" ||
                device.Read().Single(row=>row.Name=="Target").Count!=1 || !File.ReadAllBytes(Path.Combine(root,"Target.m3u")).SequenceEqual(Original) ||
                File.Exists(Path.Combine(root,".mmzs-production-pending.json")))throw new Exception("Interrupted recovery did not resume idempotently");
            checks++;
        }
        Console.WriteLine($"PASS: {checks} real process-kill/restart and conflict scenarios; persistent simulated device, no network.");
    }
    private sealed class FileDevice(string root):IProductionPlaylistDevice
    {
        private string PathName=>Path.Combine(root,"fake-device.json");
        public List<ZidooPlaylistRecord> Read()=>File.Exists(PathName)?JsonSerializer.Deserialize<List<ZidooPlaylistRecord>>(File.ReadAllBytes(PathName))!:new();
        private void Save(List<ZidooPlaylistRecord> rows)
        {
            string stage=PathName+".stage";File.WriteAllBytes(stage,JsonSerializer.SerializeToUtf8Bytes(rows));
            File.Move(stage,PathName,true);
        }
        public Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct)
        {ct.ThrowIfCancellationRequested();return Task.FromResult<IReadOnlyList<ZidooPlaylistRecord>>(Read());}
        public Task DeleteExactAsync(ZidooPlaylistRecord identity,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();var rows=Read();if(!rows.Remove(identity))throw new InvalidDataException("Identity changed");Save(rows);return Task.CompletedTask;}
        public Task ImportAsync(string filename,int expectedCount,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();var rows=Read();if(rows.Any(row=>row.Name==Path.GetFileNameWithoutExtension(filename)))throw new InvalidDataException("Duplicate fake playlist");rows.Add(new(Guid.NewGuid().ToString("N"),Path.GetFileNameWithoutExtension(filename),expectedCount,rows.Count));Save(rows);return Task.CompletedTask;}
    }
}
