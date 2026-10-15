// Alpha.50: read-only planning; no playlist, journal, evidence, or device writes.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

public enum RecoveryPlanDisposition
{
    CompletedHistoryNoAction,
    ManualReviewRequired,
    RemoteVerificationRequired,
    RecoveryIncompleteReviewRequired
}

public sealed record RecoveryExecutionPlan(Guid TransactionId,
    RecoveryPlanDisposition Disposition, RecoveryAuthorizationDisposition Authorization,
    JournalSequenceFinding Finding)
{
    // A plan is not permission to mutate any local or remote state.
    public bool MayExecute => false;
}

public sealed class IsolatedRecoveryExecutionPlanner
{
    private readonly IsolatedRecoveryAuthorizationService _authorization;

    public IsolatedRecoveryExecutionPlanner(IsolatedRecoveryAuthorizationService authorization)
        => _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));

    public IReadOnlyList<RecoveryExecutionPlan> Assess()
        => _authorization.Assess().Select(decision =>
            new RecoveryExecutionPlan(decision.TransactionId,
                decision.Disposition switch
                {
                    RecoveryAuthorizationDisposition.HistoricalConsistencyOnly => RecoveryPlanDisposition.CompletedHistoryNoAction,
                    RecoveryAuthorizationDisposition.DeniedRemoteVerificationPending => RecoveryPlanDisposition.RemoteVerificationRequired,
                    RecoveryAuthorizationDisposition.DeniedIncompleteRecovery => RecoveryPlanDisposition.RecoveryIncompleteReviewRequired,
                    _ => RecoveryPlanDisposition.ManualReviewRequired
                }, decision.Disposition, decision.Finding)).ToArray();
}
