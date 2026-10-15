// Alpha.48: read-only cross-check of journal, recovery evidence, and sequence events.
// Diagnostic only: never authorizes restoration or contacts Zidoo.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

public enum JournalSequenceFinding { Consistent, MissingSequence, MissingIntent, MissingRestoreEvidence,
    ConflictingIdentity, ConflictingEvidence, UnresolvedRemoteVerification, UnfinishedRecovery, OrphanSequence }

public sealed record JournalSequenceAudit(Guid TransactionId, JournalSequenceFinding Finding);

public sealed class IsolatedJournalSequenceAuditService
{
    private readonly PlaylistTransactionJournalService _journal;
    private readonly IsolatedRecoveryEvidenceStore _evidence;
    private readonly string _evidenceRoot;
    private readonly IsolatedTransactionSequenceStore _sequences;
    private readonly string _sequenceRoot;

    public IsolatedJournalSequenceAuditService(PlaylistTransactionJournalService journal,
        IsolatedRecoveryEvidenceStore evidence, string evidenceRoot,
        IsolatedTransactionSequenceStore sequences, string sequenceRoot)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceRoot);
        _evidenceRoot = Path.GetFullPath(evidenceRoot);
        _sequenceRoot = Path.GetFullPath(sequenceRoot);
        foreach (string root in new[] { _evidenceRoot, _sequenceRoot })
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Audit directory missing or redirected.");
    }

    public IReadOnlyList<JournalSequenceAudit> Assess()
    {
        // Validate all evidence, including orphan records; never silently skip corruption.
        var journal = _journal.ReadAll();
        if (journal.GroupBy(x => x.TransactionId).Any(x => x.Count() != 1))
            throw new InvalidDataException("Duplicate journal transaction identity.");
        var journalById = journal.ToDictionary(x => x.TransactionId);
        var evidence = new Dictionary<Guid, List<IsolatedRecoveryEvidenceRecord>>();
        foreach (string path in Directory.EnumerateFiles(_evidenceRoot, "*.json"))
        {
            var record = _evidence.Read(path);
            if (!journalById.ContainsKey(record.TransactionId))
                throw new InvalidDataException("Orphan recovery evidence.");
            if (!evidence.TryGetValue(record.TransactionId, out var group))
                evidence[record.TransactionId] = group = new();
            group.Add(record);
        }
        var sequenceIds = new HashSet<Guid>();
        foreach (string path in Directory.EnumerateFiles(_sequenceRoot, "*.json"))
        {
            string name = Path.GetFileName(path);
            if (name.Length != 50 || name[32] != '-' || !Guid.TryParseExact(name[..32], "N", out var id))
                throw new InvalidDataException("Invalid sequence filename.");
            sequenceIds.Add(id);
        }
        // ReadVerified validates complete chains and rejects damaged, missing, or reordered records.
        var sequenceById = new Dictionary<Guid, IReadOnlyList<IsolatedSequenceEvent>>();
        foreach (Guid id in sequenceIds)
            sequenceById[id] = _sequences.ReadVerified(id);
        var result = new List<JournalSequenceAudit>();
        foreach (var entry in journal)
        {
            evidence.TryGetValue(entry.TransactionId, out var ev);
            ev ??= new();
            sequenceById.TryGetValue(entry.TransactionId, out var seq);
            seq ??= Array.Empty<IsolatedSequenceEvent>();
            JournalSequenceFinding finding;
            if (entry.State == PlaylistJournalState.VerificationPending)
                finding = JournalSequenceFinding.UnresolvedRemoteVerification;
            else if (seq.Count == 0)
                finding = JournalSequenceFinding.MissingSequence;
            else if (seq.Any(x => !string.Equals(x.PlaylistFileName, entry.PlaylistName, StringComparison.Ordinal)) ||
                     ev.Any(x => !string.Equals(x.PlaylistFileName, entry.PlaylistName, StringComparison.Ordinal)))
                finding = JournalSequenceFinding.ConflictingIdentity;
            else if (!seq.Any(x => x.Phase == "IntentRecorded") || !ev.Any(x => x.Phase == "IntentRecorded"))
                finding = JournalSequenceFinding.MissingIntent;
            else if (ev.Select(x => x.OriginalSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 ||
                     ev.Select(x => x.CommittedSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 ||
                     seq.Count(x => x.Phase == "IntentRecorded") != 1 ||
                     seq.Count(x => x.Phase == "RestoreVerified") > 1 ||
                     (seq.Any(x => x.Phase == "RestoreVerified") && seq[0].Phase != "IntentRecorded"))
                finding = JournalSequenceFinding.ConflictingEvidence;
            else if (entry.State == PlaylistJournalState.Recovered &&
                     (!seq.Any(x => x.Phase == "RestoreVerified") || !ev.Any(x => x.Phase == "RestoreVerified")))
                finding = JournalSequenceFinding.MissingRestoreEvidence;
            else if (entry.State != PlaylistJournalState.Recovered ||
                     seq[^1].Phase != "RestoreVerified" || ev.Any(x => x.Phase == "RecoveryFailed"))
                finding = JournalSequenceFinding.UnfinishedRecovery;
            else
                finding = JournalSequenceFinding.Consistent;
            result.Add(new(entry.TransactionId, finding));
        }
        foreach (Guid id in sequenceIds)
            if (!journalById.ContainsKey(id)) result.Add(new(id, JournalSequenceFinding.OrphanSequence));
        return result.OrderBy(x => x.TransactionId).ToArray();
    }
}
