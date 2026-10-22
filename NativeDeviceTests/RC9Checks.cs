using System.Security.Cryptography;
using System.Text;
using MediaMonkeyZidooSync.Services;

// Uses real transaction/recovery classes with a device that imports the ordered file bytes.
internal static class RC9Checks
{
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    public static async Task Run(string? fixtureParent=null)
    {
        string root=Path.Combine(fixtureParent??Path.GetTempPath(),"MMZS-RC9-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original=Encoding.UTF8.GetBytes("#EXTM3U\r\n/Music/a.flac\r\n/Music/b.flac\r\n");
        byte[] candidate=Encoding.UTF8.GetBytes("#EXTM3U\r\n/Music/b.flac\r\n/Music/a.flac\r\n");
        int checks=0;
        void Check(bool result,string text){if(!result)throw new Exception("RC9: "+text);checks++;}
        string Setup(string name,out Device fake)
        {
            string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,"Target.m3u"),original);
            fake=new Device(folder,original);return folder;
        }
        string folder=Setup("Visible",out var fake);string target=Path.Combine(folder,"Target.m3u");
        await new NativeProductionTransaction(fake).ApplyAsync(folder,Path.Combine(folder,"NativeSync-fixture"),new("Target.m3u",candidate,2,Hash(original)),default);
        Check(fake.Deletes==1&&fake.Imports==1&&fake.Content.SequenceEqual(candidate),"same-count UPDATE reimports reordered tracks");
        Check(NativeProductionTransaction.Visibility(target)==0,"visible UPDATE remains visible");
// Deferred forced-reimport scenarios:
//         folder=Setup("RefusedCreate",out fake);target=Path.Combine(folder,"Target.m3u");File.SetAttributes(target,FileAttributes.Hidden);
//         try{await new NativeProductionTransaction(fake).ApplyAsync(folder,Path.Combine(folder,"NativeSync-fixture"),new("Target.m3u",candidate,2,Hash(original),true),default);throw new Exception("RC9 unsafe CREATE accepted");}catch(InvalidDataException){}
//         Check(fake.Deletes==0&&fake.Imports==0&&File.ReadAllBytes(target).SequenceEqual(original),"physical CREATE stops before file/device changes");
//         Check(!File.Exists(Path.Combine(folder,".mmzs-production-pending.json")),"refused CREATE opens no recovery transaction");
//         await new NativeProductionTransaction(fake).ApplyAsync(folder,Path.Combine(folder,"NativeSync-fixture"),new("Target.m3u",candidate,2,Hash(original),true,true),default);
//         Check(fake.Deletes==1&&fake.Imports==1&&fake.Content.SequenceEqual(candidate),"explicit repair reimports same-count hidden playlist");
//         Check(NativeProductionTransaction.Visibility(target)==(int)FileAttributes.Hidden,"repair preserves pre-existing Hidden flag");
//         folder=Setup("MatchingFileRepair",out fake);target=Path.Combine(folder,"Target.m3u");File.WriteAllBytes(target,candidate);
//         await new NativeProductionTransaction(fake).ApplyAsync(folder,Path.Combine(folder,"NativeSync-fixture"),new("Target.m3u",candidate,2,Hash(candidate),false,true),default);
//         Check(fake.Deletes==1&&fake.Imports==1&&fake.Content.SequenceEqual(candidate),"explicit repair fixes stale device even with matching visible M3U");
        foreach(bool cancel in new[]{false,true})
        {
            folder=Setup(cancel?"Cancel":"Failure",out fake);target=Path.Combine(folder,"Target.m3u");
            using var cts=new CancellationTokenSource();
            var tx=new NativeProductionTransaction(fake,point=>{if(point=="DeviceDeleted"){if(cancel){cts.Cancel();throw new OperationCanceledException(cts.Token);}throw new IOException("fixture interruption");}});
            try{await tx.ApplyAsync(folder,Path.Combine(folder,"NativeSync-fixture"),new("Target.m3u",candidate,2,Hash(original)),cts.Token);throw new Exception("RC9 interruption accepted");}catch(NativeTransactionRolledBackException){}
            Check(File.ReadAllBytes(target).SequenceEqual(original)&&fake.Content.SequenceEqual(original),"failure/cancellation restores original ordered content");
            Check(NativeProductionTransaction.Visibility(target)==0&&!File.Exists(Path.Combine(folder,".mmzs-production-pending.json")),"verified rollback keeps visible file and clears gate");
        }
        folder=Setup("VisibilityDrift",out fake);target=Path.Combine(folder,"Target.m3u");
        var drift=new NativeProductionTransaction(fake,point=>{if(point=="FilePublished"){File.SetAttributes(target,FileAttributes.Hidden);throw new IOException("fixture external visibility edit");}});
        try{await drift.ApplyAsync(folder,Path.Combine(folder,"NativeSync-fixture"),new("Target.m3u",candidate,2,Hash(original)),default);throw new Exception("RC9 metadata drift accepted");}catch(IOException){}
        Check(File.Exists(Path.Combine(folder,".mmzs-production-pending.json")),"visibility mismatch retains recovery gate");
        try{await new NativeInterruptedRecovery(fake).RecoverUnderLockAsync(folder,folder);throw new Exception("RC9 recovery accepted visibility drift");}catch(IOException){}
        Check(File.Exists(Path.Combine(folder,".mmzs-production-pending.json")),"restart recovery does not silently release visibility mismatch");
        // A retained RC9 repair receipt must not be reinterpreted as a normal count-only transaction.
        var pending=NativeInterruptedRecovery.ReadPending(folder,folder);
        var legacy=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(pending.Journal,"intent.json")))!;
        legacy["ReimportExisting"]=true;byte[] legacyBytes=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(legacy);
        File.WriteAllBytes(Path.Combine(pending.Journal,"intent.json"),legacyBytes);
        NativeInterruptedRecovery.Save(Path.Combine(folder,".mmzs-production-pending.json"),System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(pending with {IntentSHA256=Hash(legacyBytes)}));
        bool stopped=false;int operations=fake.Deletes+fake.Imports;
        try{await new NativeInterruptedRecovery(fake).RecoverUnderLockAsync(folder,folder);}catch(InvalidDataException error){stopped=error.Message.Contains("deferred");}
        Check(stopped&&fake.Deletes+fake.Imports==operations&&File.Exists(Path.Combine(folder,".mmzs-production-pending.json")),"legacy deferred-repair receipt retains its gate without device mutation");
        // CREATE without an old physical file retains the frozen identity-preservation rule.
        folder=Setup("MissingFileCreate",out fake);File.Delete(Path.Combine(folder,"Target.m3u"));var oldId=fake.Rows[0].Id;
        await new NativeProductionTransaction(fake).ApplyAsync(folder,Path.Combine(folder,"NativeSync-fixture"),new("Target.m3u",candidate,2,null,true),default);
        Check(fake.Deletes==0&&fake.Imports==0&&fake.Rows[0].Id==oldId,"missing-file frozen CREATE preserves existing identity");
        Check(NativeProductionTransaction.Visibility(Path.Combine(folder,"Target.m3u"))==0,"new CREATE remains visible");
        Console.WriteLine($"PASS: {checks} RC9 ordered-content, publication, cancellation and visibility recovery checks; fixture={root}; fake device only.");
    }
    sealed class Device(string root,byte[] content):IProductionPlaylistDevice
    {
        public List<ZidooPlaylistRecord> Rows=new(){new("original","Target",2,0)};
        public byte[] Content=content;public int Imports,Deletes;
        public Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<ZidooPlaylistRecord>>(Rows.ToArray());
        public Task DeleteExactAsync(ZidooPlaylistRecord row,CancellationToken ct){Deletes++;Rows.Remove(row);return Task.CompletedTask;}
        public Task ImportAsync(string file,int count,CancellationToken ct){Imports++;Content=File.ReadAllBytes(Path.Combine(root,file));Rows.Add(new("imported-"+Imports,"Target",count,0));return Task.CompletedTask;}
    }
}
