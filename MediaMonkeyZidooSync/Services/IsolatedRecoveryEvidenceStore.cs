// Alpha.44: sandbox-only immutable evidence records. Not production recovery authorization.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaMonkeyZidooSync.Services;

public sealed record IsolatedRecoveryEvidenceRecord(Guid TransactionId, string PlaylistFileName,
    string OriginalSha256, string CommittedSha256, string Phase, DateTimeOffset RecordedUtc);

public sealed class IsolatedRecoveryEvidenceStore
{
    private readonly string _root;
    public IsolatedRecoveryEvidenceStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root) || (File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Evidence root must be an existing, non-redirected sandbox directory.");
    }

    public string Append(IsolatedRecoveryEvidenceRecord record)
    {
        Validate(record);
        string payload = JsonSerializer.Serialize(record);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        string name = record.TransactionId.ToString("N") + "-" + Guid.NewGuid().ToString("N") + ".json";
        string path = Path.Combine(_root, name);
        // CreateNew prevents replacing earlier evidence. Flush requests durable file data;
        // directory-entry durability after sudden power loss is not guaranteed here.
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                   4096, FileOptions.WriteThrough))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new EvidenceEnvelope(payload, digest)));
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        return path;
    }

    public IsolatedRecoveryEvidenceRecord Read(string path)
    {
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), _root, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(full), "\\A[0-9a-f]{32}-[0-9a-f]{32}\\.json\\z"))
            throw new InvalidDataException("Evidence file path is not confined to evidence root.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Redirected evidence file rejected.");
        var envelope = JsonSerializer.Deserialize<EvidenceEnvelope>(RecoveryFileReader.ReadAllText(full))
            ?? throw new InvalidDataException("Missing evidence envelope.");
        if (string.IsNullOrWhiteSpace(envelope.Payload))
            throw new InvalidDataException("Missing evidence payload.");
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload)));
        if (!string.Equals(digest, envelope.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Evidence digest mismatch.");
        var record = JsonSerializer.Deserialize<IsolatedRecoveryEvidenceRecord>(envelope.Payload)
            ?? throw new InvalidDataException("Missing evidence record.");
        Validate(record);
        if (!Path.GetFileName(full).StartsWith(record.TransactionId.ToString("N") + "-", StringComparison.Ordinal))
            throw new InvalidDataException("Evidence transaction filename mismatch.");
        return record;
    }

    private static void Validate(IsolatedRecoveryEvidenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.TransactionId == Guid.Empty || string.IsNullOrWhiteSpace(record.PlaylistFileName) ||
            record.PlaylistFileName != Path.GetFileName(record.PlaylistFileName) ||
            !record.PlaylistFileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
            record.PlaylistFileName.Contains('\\') || record.PlaylistFileName.Contains(':') ||
            record.PlaylistFileName is "." or "..")
            throw new InvalidDataException("Invalid evidence identity.");
        foreach (var hash in new[] { record.OriginalSha256, record.CommittedSha256 })
            if (hash is null || !Regex.IsMatch(hash, "\\A[0-9a-fA-F]{64}\\z"))
                throw new InvalidDataException("Invalid evidence fingerprint.");
        if (record.Phase is not ("IntentRecorded" or "RestoreVerified" or "RecoveryFailed"))
            throw new InvalidDataException("Unknown recovery evidence phase.");
    }

    private sealed record EvidenceEnvelope(string Payload, string Sha256);
}
