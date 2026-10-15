// Alpha.49: read-only authorization gate. No file/device mutation is permitted.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

public enum RecoveryAuthorizationDisposition
{
    HistoricalConsistencyOnly,
    DeniedMissingOrConflictingEvidence,
    DeniedRemoteVerificationPending,
    DeniedIncompleteRecovery
}

public sealed record RecoveryAuthorizationDecision(Guid TransactionId,
    RecoveryAuthorizationDisposition Disposition, JournalSequenceFinding Finding)
{
    // This isolated prototype NEVER grants permission to restore files or contact Zidoo.
    public bool MayMutate => false;
}

public sealed class IsolatedRecoveryAuthorizationService
{
    private readonly IsolatedJournalSequenceAuditService _audit;

    public IsolatedRecoveryAuthorizationService(IsolatedJournalSequenceAuditService audit)
        => _audit = audit ?? throw new ArgumentNullException(nameof(audit));

    public IReadOnlyList<RecoveryAuthorizationDecision> Assess()
        => _audit.Assess().Select(x => new RecoveryAuthorizationDecision(x.TransactionId,
            x.Finding switch
            {
                JournalSequenceFinding.Consistent => RecoveryAuthorizationDisposition.HistoricalConsistencyOnly,
                JournalSequenceFinding.UnresolvedRemoteVerification => RecoveryAuthorizationDisposition.DeniedRemoteVerificationPending,
                JournalSequenceFinding.UnfinishedRecovery => RecoveryAuthorizationDisposition.DeniedIncompleteRecovery,
                _ => RecoveryAuthorizationDisposition.DeniedMissingOrConflictingEvidence
            }, x.Finding)).ToArray();
}
