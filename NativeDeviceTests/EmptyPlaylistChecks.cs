using System.Security.Cryptography;
using System.Text;
using MediaMonkeyZidooSync.Services;

internal static class EmptyPlaylistChecks
{
    public static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"MMZS-Empty-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target=Path.Combine(root,"EmptyPolicy.m3u"),evidence=Path.Combine(root,"Evidence");
        byte[] empty=new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("#EXTM3U\r\n")).ToArray();
        byte[] populated=Encoding.UTF8.GetBytes("#EXTM3U\r\n/Music/test.flac\r\n");
        var unrelated=new ZidooPlaylistRecord("untouched","Unrelated",2,0);
        var device=new Device(target,unrelated);
        var transaction=new NativeProductionTransaction(device);
        string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        void Verify(byte[]? content,int? count)
        {
            if(content is null?File.Exists(target):!File.ReadAllBytes(target).SequenceEqual(content))
                throw new Exception("Empty-policy authoritative file mismatch.");
            var rows=device.Rows.Where(row=>row.Name=="EmptyPolicy").ToArray();
            if(count is null?rows.Length!=0:rows.Length!=1||rows[0].Count!=count)
                throw new Exception("Empty-policy persistent state mismatch.");
            if(device.Rows.Single(row=>row.Name==unrelated.Name)!=unrelated)
                throw new Exception("Empty-policy transition changed an unrelated identity.");
            if(File.Exists(Path.Combine(root,".mmzs-production-pending.json")))
                throw new Exception("Verified empty-policy transition retained a recovery gate.");
        }
        await transaction.ApplyAsync(root,evidence,new("EmptyPolicy.m3u",empty,0,null),default);
        Verify(empty,null);
        if(device.Deletes!=0||device.Imports!=0)throw new Exception("Empty creation sent device mutations.");
        Console.WriteLine("PASS: empty creation retains M3U without persistent import.");

        device.ExpectedPublished=populated;
        await transaction.ApplyAsync(root,evidence,new("EmptyPolicy.m3u",populated,1,Hash(empty)),default);
        Verify(populated,1);
        if(device.Deletes!=0||device.Imports!=1)throw new Exception("Empty-to-populated transition did not import exactly once.");
        Console.WriteLine("PASS: empty-to-populated publishes authoritative M3U before import.");

        var original=device.Rows.Single(row=>row.Name=="EmptyPolicy");
        device.ExpectedPublished=empty;device.FailNextDelete=true;
        try
        {
            await transaction.ApplyAsync(root,evidence,new("EmptyPolicy.m3u",empty,0,Hash(populated)),default);
            throw new Exception("Failed empty transition was accepted.");
        }
        catch(NativeTransactionRolledBackException)
        {
            Verify(populated,1);
            if(device.Rows.Single(row=>row.Name=="EmptyPolicy")!=original)
                throw new Exception("Failed empty transition replaced a still-present original identity.");
        }
        Console.WriteLine("PASS: failed populated-to-empty restores original file and retains original device identity.");

        await transaction.ApplyAsync(root,evidence,new("EmptyPolicy.m3u",empty,0,Hash(populated)),default);
        Verify(empty,null);
        if(device.Imports!=1||device.Deletes!=2)throw new Exception("Populated-to-empty imported or retried unexpectedly.");
        Console.WriteLine("PASS: populated-to-empty publishes empty M3U before removing the persistent playlist.");

        int operations=device.Deletes+device.Imports;
        await transaction.ApplyAsync(root,evidence,new("EmptyPolicy.m3u",null,0,Hash(empty)),default);
        Verify(null,null);
        if(device.Deletes+device.Imports!=operations)throw new Exception("Deleting NAS-only empty playlist sent device mutations.");
        Console.WriteLine("PASS: deleting NAS-only empty playlist removes only its authoritative M3U.");
        Console.WriteLine("PASS: 5 empty-playlist scenarios; simulated device and disposable local files only. Evidence: "+root);
    }
    private sealed class Device:IProductionPlaylistDevice
    {
        private readonly string target;
        public List<ZidooPlaylistRecord> Rows=new();
        public byte[]? ExpectedPublished;
        public int Deletes,Imports;
        public bool FailNextDelete;
        public Device(string target,ZidooPlaylistRecord unrelated){this.target=target;Rows.Add(unrelated);}
        public Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct)
            =>Task.FromResult<IReadOnlyList<ZidooPlaylistRecord>>(Rows.ToArray());
        private void VerifyPublished()
        {
            if(ExpectedPublished is null||!File.ReadAllBytes(target).SequenceEqual(ExpectedPublished))
                throw new Exception("Device mutation preceded authoritative file publication.");
        }
        public Task DeleteExactAsync(ZidooPlaylistRecord row,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();VerifyPublished();Deletes++;
            if(FailNextDelete){FailNextDelete=false;throw new IOException("Simulated failure before empty-playlist deletion.");}
            if(row.Name!="EmptyPolicy"||!Rows.Remove(row))throw new Exception("Unexpected deletion target.");
            return Task.CompletedTask;
        }
        public Task ImportAsync(string filename,int count,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();VerifyPublished();
            if(filename!="EmptyPolicy.m3u"||count!=1||Rows.Any(row=>row.Name=="EmptyPolicy"))
                throw new Exception("Unexpected or empty persistent import.");
            Imports++;Rows.Add(new("new-"+Imports,"EmptyPolicy",count,Rows.Count));
            return Task.CompletedTask;
        }
    }
}
