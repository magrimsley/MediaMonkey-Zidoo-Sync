using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace MediaMonkeyZidooSync.Services;

public enum PlaylistJournalState { Prepared, CommitStarted, VerificationPending, Verified, RecoveryRequired, Recovered }

public sealed record PlaylistJournalEntry(Guid TransactionId, string PlaylistName,
    PlaylistJournalState State, DateTimeOffset UpdatedUtc, string Detail);

/// <summary>
/// Durable per-playlist journal. It does not perform playlist or device mutations.
/// RecoveryRequired is a signal for a later recovery coordinator, not automatic rollback.
/// </summary>
public sealed class PlaylistTransactionJournalService
{
    private readonly string _root;
    public PlaylistTransactionJournalService(string journalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalRoot);
        _root = Path.GetFullPath(journalRoot);
    }

    public void Write(PlaylistJournalEntry entry)
    {
        if (entry.TransactionId == Guid.Empty) throw new ArgumentException("Transaction ID required.");
        if (string.IsNullOrWhiteSpace(entry.PlaylistName)) throw new ArgumentException("Playlist name required.");
        Directory.CreateDirectory(_root);
        string destination = Path.Combine(_root, entry.TransactionId.ToString("N") + ".json");
        string temporary = Path.Combine(_root, entry.TransactionId.ToString("N") + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, entry);
                stream.Flush(flushToDisk: true);
            }
            // Replace existing journal atomically on the same volume where supported.
            // Retry only transient Windows sharing violations; other failures remain fatal.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temporary, destination, overwrite: true);
                    break;
                }
                catch (IOException ex) when (IsSharingViolation(ex) && attempt < 5)
                {
                    Thread.Sleep(40 * (attempt + 1));
                }
            }
        }
        finally
        {
            // Cleanup must not mask the original write/move exception.
            // A leftover .tmp is never treated as a committed journal entry.
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException) { /* Best-effort cleanup; preserve the original outcome. */ }
            catch (UnauthorizedAccessException) { /* Best-effort cleanup. */ }
        }
    }

    private static bool IsSharingViolation(IOException ex)
    {
        const int SharingViolation = 32;
        const int LockViolation = 33;
        int code = ex.HResult & 0xFFFF;
        return code is SharingViolation or LockViolation;
    }

    public IReadOnlyList<PlaylistJournalEntry> ReadAll()
    {
        if (!Directory.Exists(_root)) return Array.Empty<PlaylistJournalEntry>();
        var entries = new List<PlaylistJournalEntry>();
        foreach (string path in Directory.EnumerateFiles(_root, "*.json"))
        {
            // A damaged journal is an error, never silently interpreted as completed.
            var entry = JsonSerializer.Deserialize<PlaylistJournalEntry>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty journal: " + Path.GetFileName(path));
            entries.Add(entry);
        }
        return entries.OrderBy(x => x.UpdatedUtc).ToArray();
    }

    public IReadOnlyList<PlaylistJournalEntry> FindIncomplete() => ReadAll().Where(x =>
        x.State is not (PlaylistJournalState.Verified or PlaylistJournalState.Recovered)).ToArray();
}
