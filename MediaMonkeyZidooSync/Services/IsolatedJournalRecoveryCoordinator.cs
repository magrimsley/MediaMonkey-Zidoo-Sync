// Alpha.43: offline, sandbox-only journal/rollback coordination prototype.
// NOT production safe: no interprocess lock, concurrent-writer protection or remote reconciliation.
using System;
using System.IO;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

public sealed class IsolatedJournalRecoveryCoordinator
{
    private const string Intent = "ISOLATED_LOCAL_ROLLBACK_INTENT_V1";
    private readonly PlaylistTransactionJournalService _journal;
    private readonly IsolatedPlaylistRollbackService _rollback;

    public IsolatedJournalRecoveryCoordinator(PlaylistTransactionJournalService journal,
        IsolatedPlaylistRollbackService rollback)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _rollback = rollback ?? throw new ArgumentNullException(nameof(rollback));
    }

    public IsolatedRollbackResult Recover(IsolatedRollbackEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        // Assess first to fail closed on malformed, duplicate or unknown journal records.
        _ = new PlaylistRecoveryCoordinator(_journal).Assess();
        var matches = _journal.ReadAll().Where(e => e.TransactionId == evidence.TransactionId).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Exactly one transaction record is required.");
        var entry = matches[0];
        if (!string.Equals(entry.PlaylistName, evidence.PlaylistFileName, StringComparison.Ordinal))
            throw new InvalidDataException("Playlist identity mismatch.");
        // Remote uncertainty must never be interpreted as local rollback authorization.
        if (entry.State != PlaylistJournalState.CommitStarted &&
            !(entry.State == PlaylistJournalState.RecoveryRequired && entry.Detail == Intent))
            throw new InvalidOperationException("Journal state does not authorize isolated rollback.");
        if (entry.State == PlaylistJournalState.CommitStarted)
            _journal.Write(entry with { State = PlaylistJournalState.RecoveryRequired,
                UpdatedUtc = DateTimeOffset.UtcNow, Detail = Intent });
        // If interrupted after file restoration, Restore returns AlreadyRestored.
        // A failed Restore deliberately leaves RecoveryRequired for diagnosis/retry.
        var result = _rollback.Restore(evidence);
        _journal.Write(entry with { State = PlaylistJournalState.Recovered,
            UpdatedUtc = DateTimeOffset.UtcNow, Detail = "ISOLATED_LOCAL_ROLLBACK_VERIFIED_V1" });
        return result;
    }
}
