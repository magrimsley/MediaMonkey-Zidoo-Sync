// 1.1-alpha.40 — safe, offline transaction staging. Never commits or contacts Zidoo.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>A single prepared playlist candidate, stored outside authoritative playlist storage.</summary>
public sealed record PreparedPlaylistCandidate(string FileName, PlaylistTransactionAction Action,
    string CandidatePath, string Sha256);

/// <summary>Read-only transaction plan plus disposable candidate files; not permission to commit.</summary>
public sealed record PlaylistStagingResult(string StagingDirectory,
    IReadOnlyList<PreparedPlaylistCandidate> Candidates,
    MediaMonkeyExportTransactionPlan Plan);

/// <summary>
/// Creates and verifies M3U candidates in an isolated run directory.
/// No authoritative M3U or Zidoo playlist is changed.
/// </summary>
public sealed class NativePlaylistStagingCoordinator
{
    private readonly MediaMonkeyPlaylistExporter _exporter;
    private readonly MediaMonkeyExportTransactionPlanner _planner;

    public NativePlaylistStagingCoordinator(
        MediaMonkeyPlaylistExporter? exporter = null,
        MediaMonkeyExportTransactionPlanner? planner = null)
    {
        _exporter = exporter ?? new MediaMonkeyPlaylistExporter();
        _planner = planner ?? new MediaMonkeyExportTransactionPlanner();
    }

    public async Task<PlaylistStagingResult> PrepareAsync(
        AppConfig config, string stagingRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        // Validate everything before writing even to the isolated staging directory.
        var plan = await _planner.PrepareAsync(config, cancellationToken);
        var validation = await _exporter.ValidateAsync(config, cancellationToken);
        var prepared = validation.PreparedPlaylists.ToDictionary(x => x.M3UFile,
            StringComparer.OrdinalIgnoreCase);
        var changes = plan.Items.Where(x => x.Action is
            PlaylistTransactionAction.Create or PlaylistTransactionAction.Update).ToArray();
        foreach (var item in changes)
        {
            var gate = PlaylistTransactionPreflight.Check(plan, item.FileName);
            if (!gate.CanStage || !prepared.ContainsKey(item.FileName))
                throw new InvalidDataException("Playlist staging preflight failed: " + item.FileName);
        }

        string directory = Path.Combine(stagingRoot, "PlaylistStaging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var candidates = new List<PreparedPlaylistCandidate>();
        try
        {
            foreach (var item in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lines = prepared[item.FileName].Lines;
                string candidate = Path.Combine(directory, item.FileName);
                await File.WriteAllLinesAsync(candidate, lines, new UTF8Encoding(true), cancellationToken);
                var readBack = await File.ReadAllLinesAsync(candidate, cancellationToken);
                if (!lines.SequenceEqual(readBack, StringComparer.Ordinal))
                    throw new InvalidDataException("Staged playlist verification failed: " + item.FileName);
                byte[] digest = SHA256.HashData(await File.ReadAllBytesAsync(candidate, cancellationToken));
                candidates.Add(new(item.FileName, item.Action, candidate, Convert.ToHexString(digest)));
            }
            return new(directory, candidates.AsReadOnly(), plan);
        }
        catch
        {
            // Failed staging cannot leave partially prepared candidates behind.
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* Preserve original error; orphan cleanup needs operator review. */ }
            catch (UnauthorizedAccessException) { /* Preserve original error. */ }
            throw;
        }
    }
}
