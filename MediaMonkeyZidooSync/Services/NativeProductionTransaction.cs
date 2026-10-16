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
/// its receipt and backup and prevents a new run until explicit recovery is completed.</summary>
public sealed class NativeProductionTransaction
{
    private readonly IProductionPlaylistDevice _device;
    public NativeProductionTransaction(IProductionPlaylistDevice device) => _device = device;
    public sealed record Change(string FileName, byte[]? NewContent, int ExpectedCount, string? ExpectedOldSHA256);

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
        byte[]? old = File.Exists(target) ? await File.ReadAllBytesAsync(target, ct) : null;
        if ((old is null ? null : Hash(old)) != change.ExpectedOldSHA256)
            throw new IOException("Playlist changed since planning; transaction refused.");
        string name = Path.GetFileNameWithoutExtension(change.FileName);
        var before = await _device.InventoryAsync(ct);
        var original = before.SingleOrDefault(x => x.Name == name);
        if(change.NewContent is not null && TrackCount(change.NewContent)!=change.ExpectedCount)
            throw new InvalidDataException("Candidate M3U track count differs from the approved transaction.");
        // A persistent playlist without its original M3U cannot be compensated safely.
        if (original is not null && old is null)
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
            change.ExpectedCount, OriginalDevice = original, OriginalInventory = before
        }));
        string pending = Path.Combine(root, ".mmzs-production-pending.json");
        Durable(pending, JsonSerializer.SerializeToUtf8Bytes(new { Journal = journal, Target = target }));
        string stage = Path.Combine(root, ".mmzs-" + Guid.NewGuid().ToString("N") + ".stage");
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
            }
            if (original is not null) await _device.DeleteExactAsync(original, ct);
            if (change.NewContent is null)
            {
                VerifyOriginal();
                File.Delete(target);
            }
            else if (change.ExpectedCount > 0)
                await _device.ImportAsync(change.FileName, change.ExpectedCount, ct);
            var after = await _device.InventoryAsync(ct);
            var current = after.SingleOrDefault(x => x.Name == name);
            if (change.ExpectedCount > 0 ? current?.Count != change.ExpectedCount : current is not null)
                throw new InvalidDataException("Final device state does not match transaction.");
            var expectedHash = change.NewContent is null ? null : Hash(change.NewContent);
            if ((File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null) != expectedHash)
                throw new IOException("Final authoritative file verification failed.");
            Durable(Path.Combine(journal, "committed.json"), JsonSerializer.SerializeToUtf8Bytes(new { Time = DateTimeOffset.Now }));
            File.Delete(pending);
        }
        catch (Exception failure)
        {
            Durable(Path.Combine(journal, "failure.json"), JsonSerializer.SerializeToUtf8Bytes(new { Error = failure.ToString() }));
            try
            {
                using var rollback = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var currentInventory = await _device.InventoryAsync(rollback.Token);
                if (!before.Where(x => x.Name != name).Select(x => (x.Id,x.Name,x.Count))
                    .SequenceEqual(currentInventory.Where(x => x.Name != name).Select(x => (x.Id,x.Name,x.Count))))
                    throw new InvalidDataException("Unrelated device state changed; automatic rollback refused.");
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
                    string restore = Path.Combine(root, ".mmzs-" + Guid.NewGuid().ToString("N") + ".restore");
                    Durable(restore, old);
                    try { if (File.Exists(target)) File.Replace(restore,target,null); else File.Move(restore,target); }
                    finally { if(File.Exists(restore))File.Delete(restore); }
                }
                if (original is not null && !originalStillPresent)
                    await _device.ImportAsync(change.FileName, original.Count, rollback.Token);
                var restored = (await _device.InventoryAsync(rollback.Token)).SingleOrDefault(x => x.Name == name);
                if (original is null ? restored is not null : restored?.Count != original.Count)
                    throw new InvalidDataException("Original device state could not be verified after rollback.");
                if ((File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null) != change.ExpectedOldSHA256)
                    throw new IOException("Original file could not be verified after rollback.");
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
            if ((File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null) != change.ExpectedOldSHA256)
                throw new IOException("Authoritative file changed concurrently; mutation refused.");
        }
    }
}

public sealed class NativeTransactionRolledBackException : IOException
{
    public NativeTransactionRolledBackException(string message,Exception inner) : base(message,inner) { }
}
