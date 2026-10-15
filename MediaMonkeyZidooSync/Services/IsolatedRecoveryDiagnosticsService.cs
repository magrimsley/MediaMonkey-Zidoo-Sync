// Alpha.52: read-only operational diagnostics. No recovery execution or state mutation.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

public enum RecoveryDiagnosticStatus
{
    Completed,
    RemoteVerificationPending,
    RecoveryIncomplete,
    ManualReviewRequired
}

public sealed record RecoveryDiagnosticItem(Guid TransactionId,
    RecoveryDiagnosticStatus Status, RecoveryPlanDisposition Plan,
    RecoveryAuthorizationDisposition Authorization, JournalSequenceFinding Finding)
{
    public bool MayExecute => false;
}

public sealed record RecoveryDiagnosticsSummary(int Total, int Completed,
    int RemoteVerificationPending, int RecoveryIncomplete, int ManualReviewRequired,
    IReadOnlyList<RecoveryDiagnosticItem> Items)
{
    public bool MayExecute => false;
}

public sealed class IsolatedRecoveryDiagnosticsService
{
    private readonly IsolatedRecoveryExecutionPlanner _planner;

    public IsolatedRecoveryDiagnosticsService(IsolatedRecoveryExecutionPlanner planner)
        => _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    public RecoveryDiagnosticsSummary Assess()
    {
        var plans = _planner.Assess() ?? throw new InvalidOperationException("Planner returned no assessment collection.");
        if (plans.Any(p => p is null || p.TransactionId == Guid.Empty || p.MayExecute))
            throw new InvalidOperationException("Invalid or executable recovery plan.");
        if (plans.Select(p => p.TransactionId).Distinct().Count() != plans.Count)
            throw new InvalidOperationException("Duplicate transaction identities in recovery plans.");
        var items = plans.Select(p => new RecoveryDiagnosticItem(p.TransactionId,
            p.Disposition switch
            {
                RecoveryPlanDisposition.CompletedHistoryNoAction when
                    p.Authorization == RecoveryAuthorizationDisposition.HistoricalConsistencyOnly &&
                    p.Finding == JournalSequenceFinding.Consistent => RecoveryDiagnosticStatus.Completed,
                RecoveryPlanDisposition.RemoteVerificationRequired => RecoveryDiagnosticStatus.RemoteVerificationPending,
                RecoveryPlanDisposition.RecoveryIncompleteReviewRequired => RecoveryDiagnosticStatus.RecoveryIncomplete,
                _ => RecoveryDiagnosticStatus.ManualReviewRequired
            }, p.Disposition, p.Authorization, p.Finding))
            .OrderBy(x => x.TransactionId).ToArray();
        return new RecoveryDiagnosticsSummary(items.Length,
            items.Count(x => x.Status == RecoveryDiagnosticStatus.Completed),
            items.Count(x => x.Status == RecoveryDiagnosticStatus.RemoteVerificationPending),
            items.Count(x => x.Status == RecoveryDiagnosticStatus.RecoveryIncomplete),
            items.Count(x => x.Status == RecoveryDiagnosticStatus.ManualReviewRequired),
            Array.AsReadOnly(items));
    }
}
