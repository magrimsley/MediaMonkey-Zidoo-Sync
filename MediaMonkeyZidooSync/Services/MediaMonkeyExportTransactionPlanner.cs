// MediaMonkey Zidoo Sync — 1.1-alpha.40
// Purpose: Read-only playlist transaction planning.
// Remarks: Never changes playlist storage or contacts Zidoo. Deletions are deferred
// until the separate Zidoo persistent-playlist deletion is verified.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Proposed operation for one playlist file.</summary>
public enum PlaylistTransactionAction { Unchanged, Create, Update, PendingZidooDelete }

/// <summary>One planned playlist operation; no mutation has occurred.</summary>
public sealed record PlaylistTransactionPlanItem(string FileName, PlaylistTransactionAction Action);

/// <summary>Read-only plan of playlist file operations.</summary>
public sealed record MediaMonkeyExportTransactionPlan(
    IReadOnlyList<PlaylistTransactionPlanItem> Items,
    int CreateCount, int UpdateCount, int UnchangedCount, int PendingZidooDeleteCount);

/// <summary>Classifies MediaMonkey playlist changes without committing them.</summary>
public sealed class MediaMonkeyExportTransactionPlanner
{
    private readonly MediaMonkeyPlaylistExporter _exporter = new();

    /// <summary>Validates all playlists before planning any change.</summary>
    public async Task<MediaMonkeyExportTransactionPlan> PrepareAsync(
        AppConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var validation = await _exporter.ValidateAsync(config, ct);
        string folder = config.NAS.PlaylistFolderUNC;
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Playlist storage is unavailable: {folder}");

        var existing = FrozenPlaylistFiles.Enumerate(folder)
            .Select(path => new { Path = path, Name = Path.GetFileName(path) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(x => x.Name!, x => x.Path, StringComparer.OrdinalIgnoreCase);

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<PlaylistTransactionPlanItem>();
        foreach (var playlist in validation.PreparedPlaylists.OrderBy(x => x.M3UFile, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!expected.Add(playlist.M3UFile))
                throw new InvalidDataException($"Duplicate playlist filename: {playlist.M3UFile}");

            if (!existing.TryGetValue(playlist.M3UFile, out string? path))
            {
                items.Add(new(playlist.M3UFile, PlaylistTransactionAction.Create));
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, ct);
            items.Add(new(playlist.M3UFile,
                bytes.SequenceEqual(FrozenM3uContent.Bytes(playlist.Lines))
                    ? PlaylistTransactionAction.Unchanged : PlaylistTransactionAction.Update));
        }

        foreach (string name in existing.Keys.Where(x => !expected.Contains(x))
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            items.Add(new(name, PlaylistTransactionAction.PendingZidooDelete));
        }

        return new MediaMonkeyExportTransactionPlan(
            items.AsReadOnly(),
            items.Count(x => x.Action == PlaylistTransactionAction.Create),
            items.Count(x => x.Action == PlaylistTransactionAction.Update),
            items.Count(x => x.Action == PlaylistTransactionAction.Unchanged),
            items.Count(x => x.Action == PlaylistTransactionAction.PendingZidooDelete));
    }
}
