// Alpha.47: isolated, append-only sequence evidence. Not authorization to mutate playlists or Zidoo.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaMonkeyZidooSync.Services;

public sealed record IsolatedSequenceEvent(Guid TransactionId, string PlaylistFileName,
    long Sequence, string Phase, string PreviousDigest, DateTimeOffset RecordedUtc);

public sealed class IsolatedTransactionSequenceStore
{
    private readonly string _root;
    private static readonly JsonSerializerOptions JsonOptions = new();
    private sealed record Envelope(string Payload, string Sha256);
    private static readonly string ZeroDigest = new('0', 64);

    public IsolatedTransactionSequenceStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root) || (File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Sequence root must be an existing, non-redirected directory.");
    }

    private static string Digest(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    private static string FileName(Guid id, long sequence) => $"{id:N}-{sequence:D12}.json";

    private static void Validate(IsolatedSequenceEvent record)
    {
        if (record.TransactionId == Guid.Empty || record.Sequence < 1 || record.Sequence > 999999999999L ||
            string.IsNullOrWhiteSpace(record.PlaylistFileName) ||
            record.PlaylistFileName != Path.GetFileName(record.PlaylistFileName) ||
            !record.PlaylistFileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
            record.PlaylistFileName.Contains('\\') || record.PlaylistFileName.Contains(':') ||
            record.PlaylistFileName is "." or ".." ||
            record.Phase is not ("IntentRecorded" or "RestoreVerified" or "RecoveryFailed") ||
            record.PreviousDigest is null || !Regex.IsMatch(record.PreviousDigest, "\\A[0-9A-F]{64}\\z"))
            throw new InvalidDataException("Invalid sequence evidence.");
    }

    private (IsolatedSequenceEvent Event, string Digest) ReadFile(string path)
    {
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), _root, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(full), "\\A[0-9a-f]{32}-[0-9]{12}\\.json\\z"))
            throw new InvalidDataException("Sequence path is not confined to its root.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Redirected sequence evidence rejected.");
        var envelope = JsonSerializer.Deserialize<Envelope>(RecoveryFileReader.ReadAllText(full), JsonOptions)
            ?? throw new InvalidDataException("Missing sequence envelope.");
        if (string.IsNullOrWhiteSpace(envelope.Payload) || string.IsNullOrWhiteSpace(envelope.Sha256))
            throw new InvalidDataException("Incomplete sequence envelope.");
        string digest = Digest(envelope.Payload);
        if (!string.Equals(digest, envelope.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Sequence digest mismatch.");
        var record = JsonSerializer.Deserialize<IsolatedSequenceEvent>(envelope.Payload, JsonOptions)
            ?? throw new InvalidDataException("Missing sequence record.");
        Validate(record);
        if (!string.Equals(Path.GetFileName(full), FileName(record.TransactionId, record.Sequence), StringComparison.Ordinal))
            throw new InvalidDataException("Sequence filename identity mismatch.");
        return (record, digest);
    }

    public IReadOnlyList<IsolatedSequenceEvent> ReadVerified(Guid transactionId)
    {
        if (transactionId == Guid.Empty) throw new ArgumentException("Transaction ID required.");
        var records = Directory.EnumerateFiles(_root, $"{transactionId:N}-*.json")
            .Select(ReadFile).OrderBy(x => x.Event.Sequence).ToArray();
        long expected = 1;
        string previous = ZeroDigest;
        string? playlist = null;
        foreach (var item in records)
        {
            if (item.Event.TransactionId != transactionId || item.Event.Sequence != expected ||
                !string.Equals(item.Event.PreviousDigest, previous, StringComparison.Ordinal) ||
                (playlist != null && !string.Equals(item.Event.PlaylistFileName, playlist, StringComparison.Ordinal)))
                throw new InvalidDataException("Sequence gap, chain mismatch, or playlist identity change.");
            playlist ??= item.Event.PlaylistFileName;
            previous = item.Digest;
            expected++;
        }
        return records.Select(x => x.Event).ToArray();
    }

    public string Append(Guid transactionId, string playlistFileName, string phase, DateTimeOffset recordedUtc)
    {
        // This is deliberately single-writer sandbox functionality. Concurrent writers must be coordinated
        // before this service can ever be used in production.
        var previous = ReadVerified(transactionId);
        string digest = ZeroDigest;
        if (previous.Count > 0)
        {
            string priorPath = Path.Combine(_root, FileName(transactionId, previous[^1].Sequence));
            digest = ReadFile(priorPath).Digest;
        }
        var next = new IsolatedSequenceEvent(transactionId, playlistFileName, previous.Count + 1L,
            phase, digest, recordedUtc);
        Validate(next);
        if (previous.Count > 0 && !string.Equals(previous[0].PlaylistFileName, playlistFileName, StringComparison.Ordinal))
            throw new InvalidDataException("Playlist identity changed.");
        string payload = JsonSerializer.Serialize(next, JsonOptions);
        string path = Path.Combine(_root, FileName(transactionId, next.Sequence));
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                   4096, FileOptions.WriteThrough))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Envelope(payload, Digest(payload)), JsonOptions));
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        return path;
    }
}
