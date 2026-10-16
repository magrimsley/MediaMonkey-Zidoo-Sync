using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Explicit recovery of a deleted original playlist using a verified,
/// already-restored authoritative M3U. Never deletes or overwrites another playlist.</summary>
public sealed class NativeProductionRecovery
{
    private readonly IProductionPlaylistDevice _device;
    public NativeProductionRecovery(IProductionPlaylistDevice device)=>_device=device;
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static void NoLink(string path)
    {
        if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Linked recovery path refused: "+path);
    }
    public async Task RecoverOriginalAsync(string root,string journal,string intentHash,string originalHash,CancellationToken ct=default)
    {
        root=Path.GetFullPath(root);journal=Path.GetFullPath(journal);
        NoLink(root);NoLink(journal);
        string lockPath=Path.Combine(root,".mmzs-production.lock");NoLink(lockPath);
        using var fileLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string intentPath=Path.Combine(journal,"intent.json");NoLink(intentPath);
        byte[] intentBytes=await File.ReadAllBytesAsync(intentPath,ct);
        if(Hash(intentBytes)!=intentHash)throw new InvalidDataException("Original transaction receipt changed.");
        using var intent=JsonDocument.Parse(intentBytes);
        var original=JsonSerializer.Deserialize<ZidooPlaylistRecord>(intent.RootElement.GetProperty("OriginalDevice").GetRawText())??throw new InvalidDataException("Original device identity missing.");
        var baseline=JsonSerializer.Deserialize<ZidooPlaylistRecord[]>(intent.RootElement.GetProperty("OriginalInventory").GetRawText())!;
        string target=intent.RootElement.GetProperty("Target").GetString()!;
        if(original.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 ||
           !string.Equals(Path.GetDirectoryName(Path.GetFullPath(target)),root.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase) ||
           !string.Equals(Path.GetFileName(target),original.Name+".m3u",StringComparison.Ordinal))
            throw new InvalidDataException("Recovery target is outside the configured playlist root.");
        if(intent.RootElement.GetProperty("ExpectedOldSHA256").GetString()!=originalHash)
            throw new InvalidDataException("Original file fingerprint differs from reviewed recovery.");
        string backup=Path.Combine(journal,"original.m3u");NoLink(backup);NoLink(target);
        if(Hash(await File.ReadAllBytesAsync(backup,ct))!=originalHash || Hash(await File.ReadAllBytesAsync(target,ct))!=originalHash)
            throw new IOException("Original backup or restored authoritative file differs from the reviewed original.");
        if(File.ReadAllLines(target).Count(line=>!string.IsNullOrWhiteSpace(line)&&!line.StartsWith('#'))!=original.Count)
            throw new InvalidDataException("Original M3U cannot reproduce the original device track count.");
        string pending=Path.Combine(root,".mmzs-production-pending.json");NoLink(pending);
        byte[]? pendingBytes=File.Exists(pending)?await File.ReadAllBytesAsync(pending,ct):null;
        if(pendingBytes is not null)
        {
            using var marker=JsonDocument.Parse(pendingBytes);
            if(!string.Equals(marker.RootElement.GetProperty("Journal").GetString(),journal,StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(marker.RootElement.GetProperty("Target").GetString(),target,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pending recovery belongs to a different transaction.");
        }
        else if(!File.Exists(Path.Combine(journal,"recovered-original.json")))
            throw new InvalidDataException("Pending recovery marker is missing; no device commands issued.");
        var before=await _device.InventoryAsync(ct);
        CheckOthers(before);
        var existing=before.SingleOrDefault(x=>x.Name==original.Name);
        if(existing is not null && existing.Count!=original.Count)
            throw new InvalidDataException("Existing target differs from original state; recovery will not delete it.");
        if(existing is null)
        {
            if(pendingBytes is null)throw new InvalidDataException("Cannot import without original recovery marker.");
            await _device.ImportAsync(Path.GetFileName(target),original.Count,ct);
        }
        var after=await _device.InventoryAsync(ct);CheckOthers(after);
        var restored=after.SingleOrDefault(x=>x.Name==original.Name);
        if(restored?.Count!=original.Count || Hash(await File.ReadAllBytesAsync(target,ct))!=originalHash)
            throw new IOException("Restored device/file state could not be verified. Recovery gate retained.");
        string record=Path.Combine(journal,"recovered-original.json");
        byte[] receipt=JsonSerializer.SerializeToUtf8Bytes(new {Status="PASS",OriginalId=original.Id,RestoredId=restored.Id,original.Name,Tracks=restored.Count,OriginalSHA256=originalHash,OtherPlaylistsUnchanged=true,Time=DateTimeOffset.Now});
        using(var stream=new FileStream(record,FileMode.Create,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough))
        {await stream.WriteAsync(receipt,ct);stream.Flush(true);}
        if(pendingBytes is not null)
        {
            if(Hash(await File.ReadAllBytesAsync(pending,ct))!=Hash(pendingBytes))throw new IOException("Recovery marker changed; retained.");
            File.Delete(pending);
        }

        void CheckOthers(IReadOnlyList<ZidooPlaylistRecord> actual)
        {
            if(!baseline.Where(x=>x.Name!=original.Name).Select(x=>(x.Id,x.Name,x.Count))
                .SequenceEqual(actual.Where(x=>x.Name!=original.Name).Select(x=>(x.Id,x.Name,x.Count))))
                throw new InvalidDataException("Unrelated playlist identities/counts differ from original baseline; recovery stopped.");
        }
    }
}
