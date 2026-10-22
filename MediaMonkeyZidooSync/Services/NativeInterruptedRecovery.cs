using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MediaMonkeyZidooSync.Services;

/// <summary>
/// Reconciles a durable interrupted playlist transaction while the caller holds the
/// production lock. A verified final state is kept; an incomplete change is restored.
/// Recovery never deletes a device playlist whose provenance is uncertain.
/// </summary>
public sealed class NativeInterruptedRecovery(IProductionPlaylistDevice device, Action<string>? checkpoint = null)
{
    public sealed record Pending(int Schema, string Journal, string Target, string IntentSHA256, string CollateralSHA256);
    private sealed record Intent(string Target, string? ExpectedOldSHA256, string? NewSHA256,
        int ExpectedCount, ZidooPlaylistRecord? OriginalDevice, ZidooPlaylistRecord[] OriginalInventory, int? OriginalVisibility = null, bool ReimportExisting = false);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static int Tracks(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')
        .Split(new[]{"\r\n","\n","\r"},StringSplitOptions.None).Count(line=>!string.IsNullOrWhiteSpace(line)&&!line.StartsWith('#'));
    internal static void NoLink(string path)
    {
        for(string? current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Linked recovery storage refused.");
    }
    internal static void Save(string path, byte[] bytes)
    {
        NoLink(path);
        string stage=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        using(var stream=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough))
        {stream.Write(bytes);stream.Flush(true);}
        try{if(File.Exists(path))File.Replace(stage,path,null);else File.Move(stage,path);}
        finally{if(File.Exists(stage))File.Delete(stage);}
    }
    public static Pending ReadPending(string root,string reports)
    {
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        reports=Path.GetFullPath(reports).TrimEnd(Path.DirectorySeparatorChar);
        string path=Path.Combine(root,".mmzs-production-pending.json");NoLink(path);
        var pending=JsonSerializer.Deserialize<Pending>(File.ReadAllBytes(path))??throw new InvalidDataException("Recovery marker missing.");
        string journal=Path.GetFullPath(pending.Journal);string? run=Path.GetDirectoryName(journal);
        if(pending.Schema!=2 || string.IsNullOrEmpty(pending.IntentSHA256) || string.IsNullOrEmpty(pending.CollateralSHA256) ||
            run is null || !string.Equals(Path.GetDirectoryName(run),reports,StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(run).StartsWith("NativeSync-",StringComparison.Ordinal) ||
            !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(journal),"^Transaction-[0-9a-f]{32}$") ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(pending.Target)),root,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Interrupted transaction is not bound to the configured storage and reports.");
        NoLink(journal);NoLink(pending.Target);
        Verify(Path.Combine(journal,"intent.json"),pending.IntentSHA256);
        Verify(Path.Combine(journal,"collateral-backups.json"),pending.CollateralSHA256);
        return pending;
    }
    private static byte[] Verify(string path,string hash)
    {NoLink(path);byte[] bytes=File.ReadAllBytes(path);if(Hash(bytes)!=hash)throw new InvalidDataException("Recovery evidence changed.");return bytes;}

    /// <summary>The caller must retain the exclusive production lock for this entire operation.</summary>
    public async Task<string> RecoverUnderLockAsync(string root,string reports,CancellationToken ct=default)
    {
        var pending=ReadPending(root,reports);
        string marker=Path.Combine(root,".mmzs-production-pending.json");byte[] markerBytes=File.ReadAllBytes(marker);
        var intent=JsonSerializer.Deserialize<Intent>(Verify(Path.Combine(pending.Journal,"intent.json"),pending.IntentSHA256))
            ??throw new InvalidDataException("Invalid transaction intent.");
        string name=Path.GetFileNameWithoutExtension(pending.Target);
        if(intent.Target!=pending.Target || !pending.Target.EndsWith(".m3u",StringComparison.OrdinalIgnoreCase) ||
            intent.ExpectedCount<0 || intent.OriginalInventory is null ||
            intent.OriginalInventory.Select(row=>row.Id).Distinct().Count()!=intent.OriginalInventory.Length ||
            intent.OriginalInventory.Select(row=>row.Name).Distinct().Count()!=intent.OriginalInventory.Length ||
            intent.OriginalDevice!=intent.OriginalInventory.SingleOrDefault(row=>row.Name==name))
            throw new InvalidDataException("Inconsistent recovery intent.");
        byte[]? old=intent.ExpectedOldSHA256 is null?null:Verify(Path.Combine(pending.Journal,"original.m3u"),intent.ExpectedOldSHA256);
        byte[]? candidate=intent.NewSHA256 is null?null:Verify(Path.Combine(pending.Journal,"candidate.m3u"),intent.NewSHA256);
        if(candidate is not null && Tracks(candidate)!=intent.ExpectedCount ||
            intent.OriginalDevice is not null && old is not null && Tracks(old)!=intent.OriginalDevice.Count)
            throw new InvalidDataException("Recovery content cannot reproduce the recorded track counts.");
        string? CurrentHash(){NoLink(pending.Target);return File.Exists(pending.Target)?Hash(File.ReadAllBytes(pending.Target)):null;}
        if (intent.OriginalVisibility is int visibility && (visibility & ~((int)FileAttributes.Hidden | (int)FileAttributes.System)) != 0)
            throw new InvalidDataException("Invalid original playlist visibility in recovery receipt.");
        var currentHash=CurrentHash();
        if(currentHash!=intent.ExpectedOldSHA256 && currentHash!=intent.NewSHA256)
            throw new IOException("Playlist changed outside synchronization; recovery retained.");
        var before=await device.InventoryAsync(ct);
        var expectedOthers=intent.OriginalInventory.Where(row=>row.Name!=name).ToArray();
        var actualOthers=before.Where(row=>row.Name!=name).ToArray();
        if(actualOthers.Any(row=>!expectedOthers.Any(expected=>expected.Id==row.Id && expected.Name==row.Name && expected.Count==row.Count)))
            throw new InvalidDataException("Other playlists changed; automatic recovery refused.");
        using var collateral=JsonDocument.Parse(Verify(Path.Combine(pending.Journal,"collateral-backups.json"),pending.CollateralSHA256));
        var repaired=new HashSet<string>();
        foreach(var missing in expectedOthers.Where(row=>!actualOthers.Any(actual=>actual.Id==row.Id)))
        {
            var entries=collateral.RootElement.EnumerateArray().Where(item=>item.GetProperty("Id").GetString()==missing.Id).ToArray();
            if(entries.Length!=1)throw new InvalidDataException("Missing playlist has no unique verified backup.");
            var entry=entries[0];string filename=entry.GetProperty("Backup").GetString()!;
            if(filename!=Path.GetFileName(filename) || missing.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 ||
                entry.GetProperty("Name").GetString()!=missing.Name || entry.GetProperty("Count").GetInt32()!=missing.Count)
                throw new InvalidDataException("Invalid collateral recovery entry.");
            byte[] content=Verify(Path.Combine(pending.Journal,"Collateral",filename),entry.GetProperty("SHA256").GetString()!);
            string path=Path.Combine(root,missing.Name+".m3u");NoLink(path);
            if(Tracks(content)!=missing.Count || !File.Exists(path) || Hash(File.ReadAllBytes(path))!=Hash(content))
                throw new IOException("Authoritative collateral playlist changed; recovery retained.");
            await device.ImportAsync(missing.Name+".m3u",missing.Count,ct);
            if(Hash(File.ReadAllBytes(path))!=Hash(content))throw new IOException("Collateral file changed during recovery.");
            repaired.Add(missing.Name);
        }
        var inventory=await device.InventoryAsync(ct);CheckOthers(inventory);
        var target=inventory.SingleOrDefault(row=>row.Name==name);
        bool finalDevice=intent.ExpectedCount==0?target is null:target?.Count==intent.ExpectedCount;
        // An unchanged count/hash cannot prove that an explicitly requested reimport occurred.
        if(intent.ReimportExisting && target?.Id==intent.OriginalDevice?.Id)finalDevice=false;
        string outcome;
        if(currentHash==intent.NewSHA256 && finalDevice)
            outcome="Completed";
        else
        {
            bool originalPresent=intent.OriginalDevice is not null && target?.Count==intent.OriginalDevice.Count &&
                (target.Id==intent.OriginalDevice.Id || currentHash==intent.ExpectedOldSHA256);
            if(target is not null && !originalPresent)
                throw new InvalidDataException("Interrupted target state is uncertain; no device playlist was deleted.");
            if(CurrentHash()!=currentHash)throw new IOException("Playlist changed during recovery.");
            if(old is null){if(File.Exists(pending.Target))File.Delete(pending.Target);}
            else Save(pending.Target,old);
            checkpoint?.Invoke("FileRestored");
            if(intent.OriginalDevice is not null && !originalPresent)
            {
                if(old is null)throw new InvalidDataException("Original device playlist has no authoritative recovery content.");
                await device.ImportAsync(Path.GetFileName(pending.Target),intent.OriginalDevice.Count,ct);
            }
            checkpoint?.Invoke("DeviceRestored");
            inventory=await device.InventoryAsync(ct);CheckOthers(inventory);
            target=inventory.SingleOrDefault(row=>row.Name==name);
            if((intent.OriginalDevice is null?target is not null:target?.Count!=intent.OriginalDevice.Count) || CurrentHash()!=intent.ExpectedOldSHA256)
                throw new IOException("Restored transaction could not be verified.");
            outcome="Restored";
        }
        if (File.Exists(pending.Target) && intent.OriginalVisibility is int originalVisibility)
            NativeProductionTransaction.VerifyVisibility(pending.Target,originalVisibility);
        Save(Path.Combine(pending.Journal,"recovered-interruption.json"),JsonSerializer.SerializeToUtf8Bytes(new{Status="PASS",Outcome=outcome,Time=DateTimeOffset.Now,Inventory=inventory}));
        checkpoint?.Invoke("ReceiptSaved");
        if(Hash(File.ReadAllBytes(marker))!=Hash(markerBytes))throw new IOException("Recovery marker changed; retained.");
        File.Delete(marker);return outcome;

        void CheckOthers(IReadOnlyList<ZidooPlaylistRecord> actual)
        {
            var others=actual.Where(row=>row.Name!=name).ToArray();
            if(others.Length!=expectedOthers.Length || expectedOthers.Any(row=>!others.Any(item=>
                item.Name==row.Name && item.Count==row.Count && (item.Id==row.Id || repaired.Contains(row.Name)))))
                throw new InvalidDataException("Unrelated playlist restoration could not be verified.");
        }
    }
}
