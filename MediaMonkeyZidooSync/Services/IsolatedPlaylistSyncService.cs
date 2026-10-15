using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Security.Cryptography;

namespace MediaMonkeyZidooSync.Services;

/// <summary>
/// Deliberately independent, offline file mutation harness. It cannot contact a device,
/// cannot delete playlists, and cannot target a directory outside the OS temp directory.
/// This is NOT a production synchronization implementation.
/// </summary>
public sealed class IsolatedPlaylistSyncService
{
    private const string Marker = ".mmzs-isolated-test-only";
    private readonly Action<string>? _checkpoint;
    public IsolatedPlaylistSyncService(Action<string>? checkpoint = null) => _checkpoint = checkpoint;

    public sealed record PlaylistChange(string FileName, string Content);
    public sealed record SyncOutcome(int Created, int Updated, int Unchanged);

    public static void InitializeSandbox(string root)
    {
        var full = ValidateTempRoot(root);
        Directory.CreateDirectory(full);
        RejectReparsePoints(full);
        var marker = Path.Combine(full, Marker);
        if (!File.Exists(marker))
            using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
    }

    public SyncOutcome Apply(string root, IReadOnlyCollection<PlaylistChange> changes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var full = ValidateTempRoot(root);
        if (!Directory.Exists(full) || !File.Exists(Path.Combine(full, Marker)))
            throw new InvalidOperationException("Sandbox must be initialized explicitly before writing.");
        RejectReparsePoints(full);
        // Fail closed when another transaction owns this sandbox. FileShare.None
        // coordinates across service instances and separate processes.
        var lockPath = Path.Combine(full, ".mmzs-transaction.lock");
        if (File.Exists(lockPath)) RejectReparsePoints(lockPath);
        using var transactionLock = new FileStream(
            lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RecoverInterruptedTransactions(full, _checkpoint);
        if (changes.Count > 10000) throw new ArgumentException("Too many playlist changes.", nameof(changes));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<(string target, byte[] content)>();
        foreach (var item in changes)
        {
            ct.ThrowIfCancellationRequested();
            if (item is null || string.IsNullOrWhiteSpace(item.FileName) ||
                item.FileName != Path.GetFileName(item.FileName) ||
                item.FileName is "." or ".." ||
                item.FileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                !item.FileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(item.FileName))
                throw new ArgumentException("Invalid or duplicate playlist filename.", nameof(changes));
            ArgumentNullException.ThrowIfNull(item.Content);
            var content = new UTF8Encoding(false, true).GetBytes(item.Content);
            if (content.Length > 8 * 1024 * 1024) throw new ArgumentException("Playlist exceeds 8 MiB limit.");
            var target = Path.Combine(full, item.FileName);
            if (File.Exists(target)) RejectReparsePoints(target);
            items.Add((target, content));
        }

        // Stage all changes before publishing any target. Backups remain until commit.
        var transaction = Path.Combine(full, ".mmzs-txn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        var originals = new List<(string target, string? backup, string publishedHash)>();
        int created = 0, updated = 0, unchanged = 0;
        bool committed = false;
        bool rollbackSucceeded = false;
        try
        {
            var staged = new List<(string target, string staged, string? backup)>();
            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (target, content) = items[i];
                if (File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(content))
                {
                    unchanged++;
                    continue;
                }
                var stage = Path.Combine(transaction, staged.Count + ".new");
                File.WriteAllBytes(stage, content);
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = Path.Combine(transaction, staged.Count + ".bak");
                    File.Copy(target, backup, false);
                }
                staged.Add((target, stage, backup));
                _checkpoint?.Invoke("staged");
            }
            // Write the complete undo journal before the first mutation.
            // A restart can restore all original files, including an interrupted publish.
            var manifest = staged.Select(x => new JournalEntry(
                Path.GetFileName(x.target),
                x.backup is not null,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.staged))))).ToArray();
            WriteDurable(Path.Combine(transaction, "journal.json"),
                JsonSerializer.SerializeToUtf8Bytes(manifest));
            _checkpoint?.Invoke("journaled");
            foreach (var (target, stage, backup) in staged)
            {
                ct.ThrowIfCancellationRequested();
                // No external writers are allowed to change sandbox files during a transaction.
                if (backup is null && File.Exists(target)) throw new IOException("Target appeared during transaction.");
                if (backup is not null && (!File.Exists(target) ||
                    !File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(backup))))
                    throw new IOException("Target changed during transaction.");
                originals.Add((target, backup, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stage)))));
                File.Move(stage, target, overwrite: backup is not null);
                if (backup is null) created++; else updated++;
                _checkpoint?.Invoke("published");
            }
            ct.ThrowIfCancellationRequested();
            // A durable commit marker separates completed transactions from interrupted ones.
            WriteDurable(Path.Combine(transaction, "committed"),
                Encoding.ASCII.GetBytes("committed"));
            committed = true;
            return new SyncOutcome(created, updated, unchanged);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            for (int i = originals.Count - 1; i >= 0; i--)
            {
                var (target, backup, publishedHash) = originals[i];
                try
                {
                    // Never undo an unrelated writer's change during rollback.
                    if (!File.Exists(target) ||
                        !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))
                            .Equals(publishedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Rollback conflict: published playlist changed; evidence retained.");
                    if (backup is null) File.Delete(target);
                    else File.Copy(backup, target, overwrite: true);
                }
                catch (Exception ex) { failures.Add(ex); }
            }
            rollbackSucceeded = failures.Count == 1;
            if (!rollbackSucceeded)
                throw new AggregateException("Rollback failed; sandbox requires inspection before reuse.", failures);
            throw;
        }
        finally
        {
            // Do not erase recovery backups if rollback failed.
            if (committed || rollbackSucceeded)
            {
                try { Directory.Delete(transaction, recursive: true); }
                catch (IOException) { /* orphan staging remains inside sandbox for inspection */ }
                catch (UnauthorizedAccessException) { /* same */ }
            }
        }
    }

    private sealed record JournalEntry(string FileName, bool Existed, string? PublishedSha256 = null);

    private static void WriteDurable(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Recover uncommitted, journaled transactions while holding the sandbox lock.
    /// Invalid or incomplete journals fail closed and remain on disk for inspection.
    /// This is an offline sandbox recovery mechanism, not a production recovery guarantee.
    /// </summary>
    private static void RecoverInterruptedTransactions(string root, Action<string>? checkpoint)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, ".mmzs-txn-*").OrderBy(x => x))
        {
            RejectReparsePoints(dir);
            var journal = Path.Combine(dir, "journal.json");
            var committed = Path.Combine(dir, "committed");
            if (File.Exists(committed))
            {
                RejectReparsePoints(committed);
                if (File.ReadAllText(committed, Encoding.ASCII) != "committed")
                    throw new InvalidDataException("Incomplete transaction commit marker.");
                Directory.Delete(dir, recursive: true);
                continue;
            }
            if (!File.Exists(journal))
            {
                // No target is modified before the durable journal is created.
                // A transaction interrupted during staging is safe to discard.
                if (Directory.EnumerateFiles(dir).Any(x =>
                    !Path.GetFileName(x).EndsWith(".new", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(x).EndsWith(".bak", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Unrecognized unjournaled transaction content.");
                Directory.Delete(dir, recursive: true);
                continue;
            }
            RejectReparsePoints(journal);
            JournalEntry[] entries;
            try
            {
                entries = JsonSerializer.Deserialize<JournalEntry[]>(File.ReadAllBytes(journal))
                    ?? throw new InvalidDataException("Empty transaction journal.");
            }
            catch (JsonException ex) { throw new InvalidDataException("Invalid transaction journal.", ex); }
            if (entries.Length > 10000) throw new InvalidDataException("Transaction journal exceeds limit.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Length; i++)
            {
                var item = entries[i];
                if (item is null || string.IsNullOrWhiteSpace(item.FileName) ||
                    item.FileName != Path.GetFileName(item.FileName) ||
                    item.FileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                    !item.FileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(item.FileName))
                    throw new InvalidDataException("Unsafe transaction journal filename.");
                var target = Path.Combine(root, item.FileName);
                if (File.Exists(target)) RejectReparsePoints(target);
                if (item.Existed)
                {
                    var backup = Path.Combine(dir, i + ".bak");
                    if (!File.Exists(backup)) throw new InvalidDataException("Missing recovery backup.");
                    RejectReparsePoints(backup);
                }
                // An interrupted transaction may have published this target or may
                // not have reached it. An unrelated edit must never be overwritten.
                if (item.PublishedSha256 is null || item.PublishedSha256.Length != 64 ||
                    !item.PublishedSha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Recovery journal lacks a valid published-content digest.");
                if (File.Exists(target))
                {
                    var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
                    var matchesPublished = actual.Equals(item.PublishedSha256, StringComparison.OrdinalIgnoreCase);
                    var matchesOriginal = item.Existed && actual.Equals(
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, i + ".bak")))),
                        StringComparison.OrdinalIgnoreCase);
                    if (!matchesPublished && !matchesOriginal)
                        throw new InvalidDataException("Playlist changed independently after interruption; manual recovery required.");
                }
                else if (item.Existed)
                    throw new InvalidDataException("Original playlist disappeared during interruption; manual recovery required.");
            }
            // Restore in reverse order. Retain journal/backups if restoration fails.
            // Checkpoints are for isolated fault-injection tests only.
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                checkpoint?.Invoke("recovery-before-restore");
                var target = Path.Combine(root, entries[i].FileName);
                if (entries[i].Existed)
                    File.Copy(Path.Combine(dir, i + ".bak"), target, overwrite: true);
                else if (File.Exists(target))
                    File.Delete(target);
                checkpoint?.Invoke("recovery-after-restore");
            }
            checkpoint?.Invoke("recovery-before-cleanup");
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string ValidateTempRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Sandbox root is required.", nameof(root));
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            full.Equals(temp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Isolated sync may write only inside a child of the OS temp directory.");
        // Every existing path segment must be a real directory, not a junction/symlink.
        var cursor = full;
        while (cursor.Length >= temp.Length)
        {
            if (Directory.Exists(cursor)) RejectReparsePoints(cursor);
            if (cursor.Equals(temp, StringComparison.OrdinalIgnoreCase)) break;
            cursor = Path.GetDirectoryName(cursor) ?? breakPath();
        }
        return full;
        static string breakPath() => throw new InvalidOperationException("Invalid sandbox path.");
    }
    private static void RejectReparsePoints(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Symlinks and junctions are prohibited in the isolated sandbox.");
    }
}
