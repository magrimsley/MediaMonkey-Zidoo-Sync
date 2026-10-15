// 1.1-alpha.41 — fail-closed, offline recovery assessment. No authoritative or remote writes.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

public enum PlaylistRecoveryDisposition
{
    NoActionRequired,
    StagingCleanupReview,
    LocalCommitReview,
    RemoteVerificationRequired,
    ManualInterventionRequired
}

public sealed record PlaylistRecoveryAssessment(Guid TransactionId, string PlaylistName,
    PlaylistJournalState JournalState, PlaylistRecoveryDisposition Disposition,
    string Explanation, DateTimeOffset JournalUpdatedUtc);

/// <summary>
/// Inspects durable transaction evidence without modifying playlists or contacting Zidoo.
/// A journal state alone cannot prove whether a filesystem or remote mutation happened.
/// </summary>
public sealed class PlaylistRecoveryCoordinator
{
    private readonly PlaylistTransactionJournalService _journal;

    public PlaylistRecoveryCoordinator(PlaylistTransactionJournalService journal)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    public IReadOnlyList<PlaylistRecoveryAssessment> Assess()
    {
        // ReadAll fails closed on corrupt JSON. Do not hide or skip damaged records.
        var entries = _journal.ReadAll();
        var duplicateIds = entries.GroupBy(x => x.TransactionId)
            .Where(x => x.Count() != 1).Select(x => x.Key).ToHashSet();
        var assessments = new List<PlaylistRecoveryAssessment>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.TransactionId == Guid.Empty || string.IsNullOrWhiteSpace(entry.PlaylistName))
                throw new InvalidOperationException("Invalid transaction identity in journal.");
            if (!Enum.IsDefined(entry.State))
                throw new InvalidOperationException($"Unknown journal state for {entry.TransactionId}.");
            if (duplicateIds.Contains(entry.TransactionId))
                throw new InvalidOperationException($"Duplicate transaction identity: {entry.TransactionId}.");
            var (disposition, explanation) = entry.State switch
            {
                PlaylistJournalState.Prepared => (PlaylistRecoveryDisposition.StagingCleanupReview,
                    "Candidate staging may exist; inspect and verify ownership before cleanup."),
                PlaylistJournalState.CommitStarted => (PlaylistRecoveryDisposition.LocalCommitReview,
                    "Local commit may be partially applied. Verify destination, original backup, and hashes before compensation."),
                PlaylistJournalState.VerificationPending => (PlaylistRecoveryDisposition.RemoteVerificationRequired,
                    "Commit outcome is not proof of remote success. Reconcile Zidoo state before any retry or rollback."),
                PlaylistJournalState.RecoveryRequired => (PlaylistRecoveryDisposition.ManualInterventionRequired,
                    "Recovery is required, but journal lacks verified backup and remote-operation evidence."),
                PlaylistJournalState.Verified => (PlaylistRecoveryDisposition.NoActionRequired,
                    "Journal marks verified; no recovery action requested."),
                PlaylistJournalState.Recovered => (PlaylistRecoveryDisposition.NoActionRequired,
                    "Journal marks recovered; no recovery action requested."),
                _ => throw new InvalidOperationException("Unsupported transaction state.")
            };
            assessments.Add(new(entry.TransactionId, entry.PlaylistName, entry.State,
                disposition, explanation, entry.UpdatedUtc));
        }
        return assessments.AsReadOnly();
    }
}
