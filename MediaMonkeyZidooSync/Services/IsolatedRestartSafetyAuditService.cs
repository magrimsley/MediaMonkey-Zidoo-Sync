// Alpha.46: isolated read-only consistency audit. This is not recovery authorization.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

public enum RestartSafetyFinding
{
    None,
    RecoveredWithoutRestoreEvidence,
    RestoreEvidenceWithoutIntent,
    RemoteVerificationUnresolved,
    NonMonotonicEvidenceTimestamps,
    EvidenceAfterJournalCompletion,
    RequiresReview
}

public sealed record RestartSafetyAudit(Guid TransactionId, RestartSafetyFinding Finding);

public sealed class IsolatedRestartSafetyAuditService
{
    private readonly PlaylistTransactionJournalService _journal;
    private readonly IsolatedRecoveryEvidenceStore _store;
    private readonly string _evidenceRoot;

    public IsolatedRestartSafetyAuditService(PlaylistTransactionJournalService journal,
        IsolatedRecoveryEvidenceStore store, string evidenceRoot)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        _evidenceRoot = Path.GetFullPath(evidenceRoot);
        if (!Directory.Exists(_evidenceRoot) ||
            (File.GetAttributes(_evidenceRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Evidence root is missing or redirected.");
    }

    public IReadOnlyList<RestartSafetyAudit> Assess()
    {
        // Strictly validate the journal and evidence first; do not skip damaged entries.
        var reconciliation = new IsolatedRestartReconciliationService(_journal, _store, _evidenceRoot).Assess();
        var journal = _journal.ReadAll().ToDictionary(x => x.TransactionId);
        var records = new Dictionary<Guid, List<IsolatedRecoveryEvidenceRecord>>();
        foreach (var path in Directory.EnumerateFiles(_evidenceRoot, "*.json"))
        {
            var record = _store.Read(path);
            if (!records.TryGetValue(record.TransactionId, out var group))
                records[record.TransactionId] = group = new();
            group.Add(record);
        }
        var findings = new List<RestartSafetyAudit>();
        foreach (var assessment in reconciliation)
        {
            var entry = journal[assessment.TransactionId];
            records.TryGetValue(entry.TransactionId, out var group);
            group ??= new();
            bool hasIntent = group.Any(x => x.Phase == "IntentRecorded");
            bool hasRestored = group.Any(x => x.Phase == "RestoreVerified");
            RestartSafetyFinding finding;
            if (assessment.Status == RestartEvidenceStatus.InconsistentEvidence ||
                assessment.Status == RestartEvidenceStatus.RecoveryFailedNeedsReview)
                finding = RestartSafetyFinding.RequiresReview;
            else if (entry.State == PlaylistJournalState.VerificationPending)
                finding = RestartSafetyFinding.RemoteVerificationUnresolved;
            else if (entry.State == PlaylistJournalState.Recovered && !hasRestored)
                finding = RestartSafetyFinding.RecoveredWithoutRestoreEvidence;
            else if (hasRestored && !hasIntent)
                finding = RestartSafetyFinding.RestoreEvidenceWithoutIntent;
            else if (group.Any(x => x.RecordedUtc > entry.UpdatedUtc) &&
                     entry.State == PlaylistJournalState.Recovered)
                finding = RestartSafetyFinding.EvidenceAfterJournalCompletion;
            else if (hasIntent && hasRestored &&
                     group.Where(x => x.Phase == "RestoreVerified").Min(x => x.RecordedUtc) <
                     group.Where(x => x.Phase == "IntentRecorded").Min(x => x.RecordedUtc))
                finding = RestartSafetyFinding.NonMonotonicEvidenceTimestamps;
            else if (entry.State is PlaylistJournalState.Recovered or PlaylistJournalState.Verified &&
                     (entry.State == PlaylistJournalState.Verified || hasRestored))
                finding = RestartSafetyFinding.None;
            else
                finding = RestartSafetyFinding.RequiresReview;
            findings.Add(new(entry.TransactionId, finding));
        }
        return findings;
    }
}
