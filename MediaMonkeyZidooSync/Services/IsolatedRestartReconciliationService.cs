// Alpha.45: read-only, sandbox-only restart assessment. Never authorizes rollback.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

public enum RestartEvidenceStatus
{
    NoEvidence,
    IntentRecorded,
    RestoreVerifiedNeedsJournalReview,
    RecoveryFailedNeedsReview,
    CompletedWithEvidence,
    InconsistentEvidence
}

public sealed record RestartEvidenceAssessment(Guid TransactionId, string PlaylistName,
    PlaylistJournalState JournalState, RestartEvidenceStatus Status);

public sealed class IsolatedRestartReconciliationService
{
    private readonly PlaylistTransactionJournalService _journal;
    private readonly IsolatedRecoveryEvidenceStore _evidence;
    private readonly string _evidenceRoot;

    public IsolatedRestartReconciliationService(PlaylistTransactionJournalService journal,
        IsolatedRecoveryEvidenceStore evidence, string evidenceRoot)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        _evidenceRoot = Path.GetFullPath(evidenceRoot);
        if (!Directory.Exists(_evidenceRoot) ||
            (File.GetAttributes(_evidenceRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Evidence directory must be an existing non-redirected sandbox directory.");
    }

    public IReadOnlyList<RestartEvidenceAssessment> Assess()
    {
        // Reuse strict journal validation, including duplicate-ID and invalid-state rejection.
        _ = new PlaylistRecoveryCoordinator(_journal).Assess();
        var entries = _journal.ReadAll();
        var byId = entries.ToDictionary(x => x.TransactionId);
        var evidenceById = new Dictionary<Guid, List<IsolatedRecoveryEvidenceRecord>>();
        foreach (var path in Directory.EnumerateFiles(_evidenceRoot, "*.json"))
        {
            // Invalid evidence must fail closed; never silently skip a damaged record.
            var record = _evidence.Read(path);
            if (!byId.TryGetValue(record.TransactionId, out var entry) ||
                !string.Equals(entry.PlaylistName, record.PlaylistFileName, StringComparison.Ordinal))
                throw new InvalidDataException("Orphan or mismatched recovery evidence.");
            if (!evidenceById.TryGetValue(record.TransactionId, out var records))
                evidenceById[record.TransactionId] = records = new();
            records.Add(record);
        }
        var results = new List<RestartEvidenceAssessment>();
        foreach (var entry in entries)
        {
            evidenceById.TryGetValue(entry.TransactionId, out var records);
            RestartEvidenceStatus status;
            if (records is null || records.Count == 0) status = RestartEvidenceStatus.NoEvidence;
            else
            {
                var hashes = records.Select(r => (r.OriginalSha256, r.CommittedSha256)).Distinct().ToArray();
                if (hashes.Length != 1) status = RestartEvidenceStatus.InconsistentEvidence;
                else if (records.Any(r => r.Phase == "RecoveryFailed")) status = RestartEvidenceStatus.RecoveryFailedNeedsReview;
                else if (records.Any(r => r.Phase == "RestoreVerified"))
                    status = entry.State == PlaylistJournalState.Recovered
                        ? RestartEvidenceStatus.CompletedWithEvidence
                        : RestartEvidenceStatus.RestoreVerifiedNeedsJournalReview;
                else status = RestartEvidenceStatus.IntentRecorded;
            }
            results.Add(new(entry.TransactionId, entry.PlaylistName, entry.State, status));
        }
        return results;
    }
}
