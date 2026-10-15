// 1.1-alpha.40 — read-only folder reconciliation and coordination.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Turns a validated plan into deterministic, independently gated playlist work.</summary>
public sealed class NativePlaylistFolderReconciler
{
    public sealed record WorkItem(string FileName, PlaylistTransactionAction Action,
        bool RequiresZidooDeletionVerification);

    public IReadOnlyList<WorkItem> Reconcile(MediaMonkeyExportTransactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var work = new List<WorkItem>();
        foreach (var item in plan.Items.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
        {
            if (!names.Add(item.FileName)) throw new InvalidOperationException("Duplicate M3U filename in plan.");
            var check = PlaylistTransactionPreflight.Check(plan, item.FileName);
            if (item.Action == PlaylistTransactionAction.Unchanged) continue;
            if (!check.CanStage && !check.RequiresZidooDeletion)
                throw new InvalidOperationException("Playlist preflight rejected " + item.FileName);
            work.Add(new WorkItem(item.FileName, item.Action, check.RequiresZidooDeletion));
        }
        return work.AsReadOnly();
    }

    /// <summary>Deletion authorization requires verified exact Zidoo identity, not merely a missing file.</summary>
    public static bool CanDeleteStoredM3u(WorkItem item, bool exactZidooPlaylistDeletionVerified) =>
        item.Action == PlaylistTransactionAction.PendingZidooDelete && exactZidooPlaylistDeletionVerified;
}
