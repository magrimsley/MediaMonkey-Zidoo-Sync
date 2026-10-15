using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Read-only counts for already-loaded recovery report rows. Never authorizes execution.</summary>
public sealed record RecoveryReportDisplaySummary(int Total, int NeedsReview, int Pending, int Other)
{
    public static RecoveryReportDisplaySummary From(IReadOnlyList<RecoveryReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int review = rows.Count(x => x.Status.Contains("review", StringComparison.OrdinalIgnoreCase)
            || x.Finding.Contains("review", StringComparison.OrdinalIgnoreCase));
        int pending = rows.Count(x => !x.Status.Contains("review", StringComparison.OrdinalIgnoreCase)
            && !x.Finding.Contains("review", StringComparison.OrdinalIgnoreCase)
            && (x.Status.Contains("pending", StringComparison.OrdinalIgnoreCase)
                || x.Finding.Contains("pending", StringComparison.OrdinalIgnoreCase)
                || x.Status.Contains("verification", StringComparison.OrdinalIgnoreCase)));
        return new(rows.Count, review, pending, rows.Count - review - pending);
    }
}
