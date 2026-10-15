// 1.1-alpha.42 — offline, sandbox-confined local rollback proof of concept.
// NOT connected to production synchronization or Zidoo operations.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace MediaMonkeyZidooSync.Services;

public sealed record IsolatedRollbackEvidence(Guid TransactionId, string PlaylistFileName,
    string OriginalSha256, string CommittedSha256);

public enum IsolatedRollbackResult { Restored, AlreadyRestored }

public sealed class IsolatedPlaylistRollbackService
{
    private readonly string _sandbox;
    public IsolatedPlaylistRollbackService(string sandboxRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxRoot);
        _sandbox = Path.GetFullPath(sandboxRoot);
        if (!Directory.Exists(_sandbox)) throw new DirectoryNotFoundException(_sandbox);
        if ((File.GetAttributes(_sandbox) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Sandbox cannot be a symbolic link or reparse point.");
    }

    public IsolatedRollbackResult Restore(IsolatedRollbackEvidence evidence)
    {
        if (evidence is null) throw new ArgumentNullException(nameof(evidence));
        if (evidence.TransactionId == Guid.Empty) throw new InvalidDataException("Transaction ID required.");
        string name = evidence.PlaylistFileName;
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) ||
            name is "." or ".." || !name.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':') ||
            name.Contains('/') || name.Contains('\\'))
            throw new InvalidDataException("Unsafe playlist file name.");
        ValidateHash(evidence.OriginalSha256);
        ValidateHash(evidence.CommittedSha256);
        // Deliberately require a fresh, dedicated sandbox with only these two fixed subfolders.
        string originals = Path.Combine(_sandbox, "backups");
        string destinations = Path.Combine(_sandbox, "playlists");
        ValidateDirectory(originals);
        ValidateDirectory(destinations);
        string backup = Path.Combine(originals, evidence.TransactionId.ToString("N") + ".m3u");
        string target = Path.Combine(destinations, name);
        ValidateFile(backup);
        ValidateFile(target);
        byte[] original = File.ReadAllBytes(backup);
        if (!Matches(original, evidence.OriginalSha256))
            throw new InvalidDataException("Backup fingerprint mismatch; no restoration performed.");
        byte[] current = File.ReadAllBytes(target);
        if (Matches(current, evidence.OriginalSha256)) return IsolatedRollbackResult.AlreadyRestored;
        if (!Matches(current, evidence.CommittedSha256))
            throw new InvalidDataException("Destination changed unexpectedly; no restoration performed.");
        // A recheck narrows the race window; this isolated harness is NOT safe for live concurrent writers.
        if (!Matches(File.ReadAllBytes(target), evidence.CommittedSha256))
            throw new IOException("Destination changed during recovery.");
        string temp = Path.Combine(destinations, ".rollback-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(original);
                stream.Flush(flushToDisk: true);
            }
            if (!Matches(ReadStagedFileWithBoundedSharingRetry(temp), evidence.OriginalSha256))
                throw new IOException("Staged restoration verification failed.");
            // The current destination must remain unchanged until the replacement.
            if (!Matches(File.ReadAllBytes(target), evidence.CommittedSha256))
                throw new IOException("Destination changed before replacement.");
            File.Move(temp, target, overwrite: true);
            if (!Matches(File.ReadAllBytes(target), evidence.OriginalSha256))
                throw new IOException("Restored file failed post-write verification.");
            return IsolatedRollbackResult.Restored;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    // Only the newly staged sandbox file is retried. Do not retry target verification,
    // fingerprint failures, journal replacement, or unauthorized access.
    // Sharing violation (Win32 ERROR_SHARING_VIOLATION = 32) is transient on some hosts.
    private static byte[] ReadStagedFileWithBoundedSharingRetry(string path)
    {
        const int sharingViolationHResult = unchecked((int)0x80070020);
        for (int attempt = 1; ; attempt++)
        {
            try { return File.ReadAllBytes(path); }
            catch (IOException ex) when (ex.HResult == sharingViolationHResult && attempt < 4)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"Sandbox staged-file sharing violation; retry {attempt}/3: {Path.GetFileName(path)}");
                Thread.Sleep(40);
            }
        }
    }

    private void ValidateDirectory(string path)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Required sandbox directory is absent or redirected: " + path);
    }
    private static void ValidateFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Required sandbox file is absent or redirected: " + path);
    }
    private static void ValidateHash(string hash)
    {
        if (hash is null || hash.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(hash, "\\A[0-9a-fA-F]{64}\\z"))
            throw new InvalidDataException("Invalid SHA-256 fingerprint.");
    }
    public static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool Matches(byte[] bytes, string hash) =>
        string.Equals(Fingerprint(bytes), hash, StringComparison.OrdinalIgnoreCase);
}
