// 1.1-alpha.40 — Pure preflight gate for later per-playlist transaction execution.
// Does not modify files or contact Zidoo. Execution remains disabled.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Read-only authorization requirements before any playlist transaction can begin.</summary>
public sealed record PlaylistTransactionPreflightResult(bool CanStage, bool RequiresZidooDeletion,
    IReadOnlyList<string> Reasons);

/// <summary>Validates transaction-plan invariants; does not grant permission to commit.</summary>
public static class PlaylistTransactionPreflight
{
    public static PlaylistTransactionPreflightResult Check(
        MediaMonkeyExportTransactionPlan plan, string playlistFileName)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistFileName);
        var reasons = new List<string>();
        if (Path.GetFileName(playlistFileName) != playlistFileName ||
            playlistFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !playlistFileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
            reasons.Add("Playlist filename is not a safe M3U filename.");
        var matching = plan.Items.Where(x => string.Equals(x.FileName, playlistFileName,
            StringComparison.OrdinalIgnoreCase)).ToList();
        if (matching.Count != 1)
            reasons.Add("Exactly one matching playlist transaction is required.");
        var action = matching.Count == 1 ? matching[0].Action : PlaylistTransactionAction.Unchanged;
        bool delete = matching.Count == 1 && action == PlaylistTransactionAction.PendingZidooDelete;
        if (delete) reasons.Add("Zidoo persistent playlist deletion and exact-ID verification must precede file deletion.");
        if (matching.Count == 1 && action == PlaylistTransactionAction.Unchanged)
            reasons.Add("Unchanged playlist requires no transaction.");
        return new PlaylistTransactionPreflightResult(reasons.Count == 0 && matching.Count == 1 &&
            action is PlaylistTransactionAction.Create or PlaylistTransactionAction.Update,
            delete, reasons.AsReadOnly());
    }
}
