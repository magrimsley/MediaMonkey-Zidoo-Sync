using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Presentation-only description of a previously validated proposed operation.</summary>
public sealed record NativePlanInspectionItem(
    string FileName, PlaylistTransactionAction Action, string Explanation,
    bool RequiresZidooDeletionVerification);

/// <summary>Projects a completed read-only assessment; never replans or accesses storage.</summary>
public static class NativePlanInspection
{
    public static IReadOnlyList<NativePlanInspectionItem> FromAssessment(NativeSyncReadOnlyResult assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (assessment.MayExecute || assessment.ProposedWork is null)
            throw new InvalidOperationException("Inspection requires a valid non-executable assessment.");
        var items = assessment.ProposedWork.Select(item =>
        {
            if (item is null || string.IsNullOrWhiteSpace(item.FileName) ||
                item.Action == PlaylistTransactionAction.Unchanged ||
                !Enum.IsDefined(item.Action))
                throw new InvalidOperationException("Invalid proposed operation for inspection.");
            var reason = item.Action switch
            {
                PlaylistTransactionAction.Create => "Expected playlist file is missing from storage.",
                PlaylistTransactionAction.Update => "Stored playlist content differs from expected content.",
                PlaylistTransactionAction.PendingZidooDelete => "Stored playlist is not in the expected export; deletion requires separate Zidoo verification.",
                _ => throw new InvalidOperationException("Unknown operation.")
            };
            if (item.RequiresZidooDeletionVerification != (item.Action == PlaylistTransactionAction.PendingZidooDelete))
                throw new InvalidOperationException("Inconsistent deletion verification requirement.");
            return new NativePlanInspectionItem(item.FileName, item.Action, reason, item.RequiresZidooDeletionVerification);
        }).OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (items.Count(x => x.Action == PlaylistTransactionAction.Create) != assessment.CreateCount ||
            items.Count(x => x.Action == PlaylistTransactionAction.Update) != assessment.UpdateCount ||
            items.Count(x => x.Action == PlaylistTransactionAction.PendingZidooDelete) != assessment.PendingZidooDeleteCount)
            throw new InvalidOperationException("Inspection counts do not match assessment.");
        return new ReadOnlyCollection<NativePlanInspectionItem>(items);
    }
}
