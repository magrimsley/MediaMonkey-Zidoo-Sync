// 1.1-alpha.40 — isolated native playlist-file transaction service.
// NOT connected to production synchronization. Zidoo verification is an external prerequisite.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Stages and atomically replaces one M3U, with a durable backup for compensation.</summary>
public sealed class NativePlaylistTransactionService
{
    public sealed record Stage(string Destination, string Candidate, string Backup, bool Existed);
    private static readonly UTF8Encoding Utf8Bom = new(true);

    public async Task<Stage> StageAsync(string folder, string filename, string[] lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (Path.GetFileName(filename) != filename || filename is "." or ".." ||
            !filename.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
            filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Unsafe M3U filename.", nameof(filename));
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        string destination = Path.Combine(folder, filename);
        string id = Guid.NewGuid().ToString("N");
        string candidate = Path.Combine(folder, "." + id + ".stage");
        string backup = Path.Combine(folder, "." + id + ".backup");
        bool existed = File.Exists(destination);
        try
        {
            await File.WriteAllLinesAsync(candidate, lines, Utf8Bom, ct);
            if (existed) File.Copy(destination, backup, overwrite: false);
            return new Stage(destination, candidate, backup, existed);
        }
        catch
        {
            if (File.Exists(candidate)) File.Delete(candidate);
            if (File.Exists(backup)) File.Delete(backup);
            throw;
        }
    }

    /// <summary>Commits one staged file. Caller must coordinate and verify Zidoo separately.</summary>
    public void Commit(Stage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (!File.Exists(stage.Candidate)) throw new FileNotFoundException("Staged M3U missing.", stage.Candidate);
        if (stage.Existed)
        {
            if (!File.Exists(stage.Backup)) throw new FileNotFoundException("Recovery backup missing.", stage.Backup);
            File.Replace(stage.Candidate, stage.Destination, null);
        }
        else
        {
            if (File.Exists(stage.Destination)) throw new IOException("Destination was created after staging.");
            File.Move(stage.Candidate, stage.Destination);
        }
    }

    /// <summary>Compensates a committed change or discards an uncommitted stage.</summary>
    public void Rollback(Stage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (stage.Existed)
        {
            if (!File.Exists(stage.Backup)) throw new IOException("Cannot restore original M3U: backup missing.");
            File.Copy(stage.Backup, stage.Destination, overwrite: true);
        }
        else if (!File.Exists(stage.Candidate) && File.Exists(stage.Destination))
        {
            File.Delete(stage.Destination);
        }
        if (File.Exists(stage.Candidate)) File.Delete(stage.Candidate);
    }

    /// <summary>Removes a backup only after the external transaction is verified and committed.</summary>
    public void Complete(Stage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (File.Exists(stage.Candidate)) throw new InvalidOperationException("Stage was not committed.");
        if (File.Exists(stage.Backup)) File.Delete(stage.Backup);
    }
}
