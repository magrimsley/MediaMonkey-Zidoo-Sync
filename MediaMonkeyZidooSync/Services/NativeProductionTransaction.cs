using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MediaMonkeyZidooSync.Services;

public interface IProductionPlaylistDevice
{
    Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct);
    Task DeleteExactAsync(ZidooPlaylistRecord identity, CancellationToken ct);
    Task ImportAsync(string filename, int expectedCount, CancellationToken ct);
}

/// <summary>One durable native transaction. A failed or interrupted transaction retains
/// its receipt and backup for reconciliation before a new synchronization may begin.</summary>
public sealed class NativeProductionTransaction
{
    private readonly IProductionPlaylistDevice _device;
    private readonly Action<string>? _checkpoint;
    public NativeProductionTransaction(IProductionPlaylistDevice device, Action<string>? checkpoint = null)
    { _device = device; _checkpoint = checkpoint; }
    public sealed record Change(string FileName, byte[]? NewContent, int ExpectedCount, string? ExpectedOldSHA256, bool IsCreate = false); // Deferred repair: bool ReimportExisting = false;

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static int TrackCount(byte[] data) => Encoding.UTF8.GetString(data).TrimStart('\uFEFF')
        .Split(new[]{"\r\n","\n","\r"},StringSplitOptions.None).Count(line=>!string.IsNullOrWhiteSpace(line)&&!line.StartsWith('#'));
    private static void NoLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked playlist storage refused: " + path);
    }
    private static void Durable(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(data); stream.Flush(true);
    }
    internal static int Visibility(string path) => (int)(File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System));
    internal static void VerifyVisibility(string path, int expected)
    {
        if (Visibility(path) != expected) throw new IOException("Playlist visibility changed during synchronization: " + Path.GetFileName(path) + ". Recovery evidence was retained.");
    }
    // DEFERRED: explicit playlist reimport is outside the frozen process; see Documentation/BACKLOG.md.
//     internal static void CheckExistingCreation(Change change, ZidooPlaylistRecord? original, bool hasFile)
//     {
//         if (change.ReimportExisting && (!hasFile || original is null || change.NewContent is null || change.ExpectedCount <= 0))
//             throw new InvalidDataException("Reviewed playlist repair requires an existing M3U and an existing populated Zidoo playlist: " + change.FileName);
//         if (change.IsCreate && !change.ReimportExisting && hasFile && original is not null && change.NewContent is not null && change.ExpectedCount > 0)
//             throw new InvalidDataException("Cannot verify the existing Zidoo playlist's song order for " + change.FileName + ". Review Changes, choose Repair Existing Playlists, and explicitly select this playlist for reimport. No playlist change started for this target.");
//     }
    public async Task ApplyAsync(string playlistFolder, string evidenceFolder, Change change, CancellationToken ct)
    {
        if (change.FileName != Path.GetFileName(change.FileName) ||
            change.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !change.FileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || change.ExpectedCount < 0 ||
            (change.NewContent is null && change.ExpectedCount != 0))
            throw new ArgumentException("Invalid native transaction.");
        string root = Path.GetFullPath(playlistFolder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        NoLink(root);
        string target = Path.Combine(root, change.FileName);
        NoLink(target);
        int? originalVisibility = File.Exists(target) ? Visibility(target) : null;
        byte[]? old = File.Exists(target) ? await File.ReadAllBytesAsync(target, ct) : null;
        if ((old is null ? null : Hash(old)) != change.ExpectedOldSHA256)
            throw new IOException("Playlist changed since planning; transaction refused.");
        string name = Path.GetFileNameWithoutExtension(change.FileName);
        var before = await _device.InventoryAsync(ct);
        var original = before.SingleOrDefault(x => x.Name == name);
        if(change.NewContent is not null && TrackCount(change.NewContent)!=change.ExpectedCount)
            throw new InvalidDataException("Candidate M3U track count differs from the approved transaction.");
// Deferred repair: CheckExistingCreation(change, original, old is not null);
        bool preserveExisting = /* Deferred: !change.ReimportExisting && */ (old is null || change.IsCreate) && change.NewContent is not null && original is not null && change.ExpectedCount > 0;
        if (preserveExisting && original!.Count != change.ExpectedCount)
            throw new InvalidDataException("Existing device playlist differs from the proposed creation; no changes made.");
        // An existing matching creation is verified without deleting or reimporting it.
        if (original is not null && old is null && !preserveExisting)
            throw new InvalidDataException("Existing device playlist has no authoritative file backup.");
        if(original is not null && old is not null && TrackCount(old)!=original.Count)
            throw new InvalidDataException("Original authoritative M3U cannot reproduce the device track count; update refused.");
        Directory.CreateDirectory(evidenceFolder);
        string journal = Path.Combine(evidenceFolder, "Transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(journal);
        if (old is not null) Durable(Path.Combine(journal, "original.m3u"), old);
        if (change.NewContent is not null) Durable(Path.Combine(journal, "candidate.m3u"), change.NewContent);
        Durable(Path.Combine(journal, "intent.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            Target = target, change.ExpectedOldSHA256, NewSHA256 = change.NewContent is null ? null : Hash(change.NewContent),
            change.ExpectedCount, OriginalVisibility = originalVisibility, /* Deferred: change.ReimportExisting, */ OriginalDevice = original, OriginalInventory = before
        }));
        // Capture current authoritative content, including any earlier committed changes.
        var otherFiles = new Dictionary<string, byte[]>();
        foreach (var row in before.Where(x => x.Name != name))
        {
            string filename = row.Name + ".m3u";
            if (filename != Path.GetFileName(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            string path = Path.Combine(root, filename);NoLink(path);
            if (!File.Exists(path)) continue;
            byte[] content = await File.ReadAllBytesAsync(path, ct);
            if (TrackCount(content) != row.Count) continue;
            otherFiles.Add(row.Id, content);
        }
        string collateralFolder=Path.Combine(journal,"Collateral");Directory.CreateDirectory(collateralFolder);
        int backupIndex=0;
        var collateralManifest=new List<object>();
        foreach(var row in before.Where(x=>otherFiles.ContainsKey(x.Id)))
        {
            string backup=(backupIndex++).ToString("D4")+".m3u";
            Durable(Path.Combine(collateralFolder,backup),otherFiles[row.Id]);
            collateralManifest.Add(new{row.Id,row.Name,row.Count,Backup=backup,SHA256=Hash(otherFiles[row.Id])});
        }
        Durable(Path.Combine(journal,"collateral-backups.json"),JsonSerializer.SerializeToUtf8Bytes(collateralManifest));
        string pending = Path.Combine(root, ".mmzs-production-pending.json");
        if(File.Exists(pending))throw new IOException("A previous transaction still requires recovery.");
        NativeInterruptedRecovery.Save(pending,JsonSerializer.SerializeToUtf8Bytes(new NativeInterruptedRecovery.Pending(
            2,journal,target,Hash(File.ReadAllBytes(Path.Combine(journal,"intent.json"))),Hash(File.ReadAllBytes(Path.Combine(journal,"collateral-backups.json"))))));
        _checkpoint?.Invoke("Prepared");
        string stage = Path.Combine(root, "mmzs-" + Guid.NewGuid().ToString("N") + ".stage");
        try
        {
            // Verify the immutable backup immediately before the first mutation.
            if (old is not null && Hash(File.ReadAllBytes(Path.Combine(journal, "original.m3u"))) != change.ExpectedOldSHA256)
                throw new IOException("Original backup hash mismatch.");
            if (change.NewContent is not null)
            {
                Durable(stage, change.NewContent);
                VerifyOriginal();
                if (old is null) File.Move(stage, target); else File.Replace(stage, target, null);
                VerifyVisibility(target, originalVisibility ?? 0);
            }
            _checkpoint?.Invoke("FilePublished");
            if (original is not null && !preserveExisting) await _device.DeleteExactAsync(original, ct);
            _checkpoint?.Invoke("DeviceDeleted");
            if (change.NewContent is null)
            {
                VerifyOriginal();
                File.Delete(target);
            }
            else if (change.ExpectedCount > 0 && !preserveExisting)
                await _device.ImportAsync(change.FileName, change.ExpectedCount, ct);
            _checkpoint?.Invoke("DeviceImported");
            var after = await _device.InventoryAsync(ct);
            if (!before.Where(x => x.Name != name).Select(x => (x.Id,x.Name,x.Count))
                .SequenceEqual(after.Where(x => x.Name != name).Select(x => (x.Id,x.Name,x.Count))))
                throw new InvalidDataException("Unrelated device state changed; transaction cannot be committed.");
            var current = after.SingleOrDefault(x => x.Name == name);
            if (change.ExpectedCount > 0 ? current?.Count != change.ExpectedCount : current is not null)
                throw new InvalidDataException("Final device state does not match transaction.");
            var expectedHash = change.NewContent is null ? null : Hash(change.NewContent);
            if ((File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null) != expectedHash)
                throw new IOException("Final authoritative file verification failed.");
            if (change.NewContent is not null) VerifyVisibility(target, originalVisibility ?? 0);
            Durable(Path.Combine(journal, "committed.json"), JsonSerializer.SerializeToUtf8Bytes(new { Time = DateTimeOffset.Now }));
            _checkpoint?.Invoke("Committed");
            File.Delete(pending);
        }
        catch (Exception failure)
        {
            if (failure is OperationCanceledException && ct.IsCancellationRequested)
                Durable(Path.Combine(journal, "cancelled.json"), JsonSerializer.SerializeToUtf8Bytes(new
                { Message = "User cancelled the synchronization. Restoring the current playlist.", Time = DateTimeOffset.Now }));
            else
                Durable(Path.Combine(journal, "failure.json"), JsonSerializer.SerializeToUtf8Bytes(new { Error = failure.ToString() }));
            try
            {
                using var rollback = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var currentInventory = await _device.InventoryAsync(rollback.Token);
                var expectedOthers=before.Where(x=>x.Name!=name).ToArray();
                var actualOthers=currentInventory.Where(x=>x.Name!=name).ToArray();
                // Only an absent original identity can be repaired automatically. Edited,
                // replaced or additional playlists remain ambiguous and retain the gate.
                if(actualOthers.Any(row=>!expectedOthers.Any(oldRow=>oldRow.Id==row.Id && oldRow.Name==row.Name && oldRow.Count==row.Count)))
                    throw new InvalidDataException("Unrelated device state changed; automatic rollback refused.");
                var repaired=new List<ZidooPlaylistRecord>();
                foreach(var missing in expectedOthers.Where(row=>!actualOthers.Any(currentRow=>currentRow.Id==row.Id)))
                {
                    if(!otherFiles.TryGetValue(missing.Id,out var content))
                        throw new InvalidDataException("Missing playlist has no verified authoritative backup.");
                    string path=Path.Combine(root,missing.Name+".m3u");NoLink(path);
                    if(!File.Exists(path) || Hash(File.ReadAllBytes(path))!=Hash(content))
                        throw new IOException("Authoritative recovery content changed; collateral repair refused.");
                    await _device.ImportAsync(missing.Name+".m3u",missing.Count,rollback.Token);
                    if(Hash(File.ReadAllBytes(path))!=Hash(content))throw new IOException("Authoritative content changed during collateral repair.");
                    repaired.Add(missing);
                }
                currentInventory=await _device.InventoryAsync(rollback.Token);
                var verifiedOthers=currentInventory.Where(x=>x.Name!=name).ToArray();
                if(verifiedOthers.Length!=expectedOthers.Length || expectedOthers.Any(row=>
                    !verifiedOthers.Any(currentRow=>currentRow.Name==row.Name && currentRow.Count==row.Count &&
                        (currentRow.Id==row.Id || repaired.Any(repairedRow=>repairedRow.Id==row.Id)))))
                    throw new InvalidDataException("Collateral restoration could not be verified.");
                var current = currentInventory.SingleOrDefault(x => x.Name == name);
                var fileHash = File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null;
                var newHash = change.NewContent is null ? null : Hash(change.NewContent);
                if (fileHash != change.ExpectedOldSHA256 && fileHash != newHash)
                    throw new IOException("Playlist edited externally; automatic rollback refused.");
                bool originalStillPresent = original is not null && current?.Id == original.Id && current.Count == original.Count;
                if (current is not null && !originalStillPresent)
                    await _device.DeleteExactAsync(current, rollback.Token);
                NoLink(target);
                if (old is null) File.Delete(target);
                else
                {
                    string restore = Path.Combine(root, "mmzs-" + Guid.NewGuid().ToString("N") + ".restore");
                    Durable(restore, old);
                    try { if (File.Exists(target)) File.Replace(restore,target,null); else File.Move(restore,target); }
                    finally { if(File.Exists(restore))File.Delete(restore); }
                }
                if (original is not null && !originalStillPresent)
                    await _device.ImportAsync(change.FileName, original.Count, rollback.Token);
                var restoredInventory = await _device.InventoryAsync(rollback.Token);
                var restoredOthers=restoredInventory.Where(x=>x.Name!=name).ToArray();
                if(restoredOthers.Length!=verifiedOthers.Length || verifiedOthers.Any(row=>!restoredOthers.Any(currentRow=>currentRow.Id==row.Id && currentRow.Name==row.Name && currentRow.Count==row.Count)))
                    throw new InvalidDataException("Unrelated playlists changed during target restoration.");
                var restored = restoredInventory.SingleOrDefault(x => x.Name == name);
                if (original is null ? restored is not null : restored?.Count != original.Count)
                    throw new InvalidDataException("Original device state could not be verified after rollback.");
                if ((File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null) != change.ExpectedOldSHA256)
                    throw new IOException("Original file could not be verified after rollback.");
                if (old is not null) VerifyVisibility(target, originalVisibility!.Value);
                Durable(Path.Combine(journal,"rolled-back.json"),JsonSerializer.SerializeToUtf8Bytes(new {Time=DateTimeOffset.Now}));
                File.Delete(pending);
                throw new NativeTransactionRolledBackException("Synchronization failed; original target state was restored. Evidence: " + journal,failure);
            }
            catch (NativeTransactionRolledBackException) { throw; }
            catch (Exception recoveryFailure)
            {
                Durable(Path.Combine(journal,"recovery-failure.json"),JsonSerializer.SerializeToUtf8Bytes(new {Error=recoveryFailure.ToString()}));
            }
            // Retain the durable gate if compensation cannot be verified.
            throw new IOException("Native synchronization stopped. Recovery evidence retained at " + journal +
                ". New runs are blocked until recovery is reviewed.", failure);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }

        void VerifyOriginal()
        {
            NoLink(target);
            if ((File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null) != change.ExpectedOldSHA256 ||
                (File.Exists(target) ? Visibility(target) : (int?)null) != originalVisibility)
                throw new IOException("Authoritative file or visibility changed concurrently; mutation refused.");
        }
    }
}

public sealed class NativeTransactionRolledBackException : IOException
{
    public NativeTransactionRolledBackException(string message,Exception inner) : base(message,inner) { }
}
