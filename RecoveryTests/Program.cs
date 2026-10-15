using MediaMonkeyZidooSync.Services;
using System.Text.Json;

int passed = 0;
int failed = 0;
var root = Path.Combine(Path.GetTempPath(), "MMZS-RecoveryTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    void Test(string name, Action body)
    {
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { body(); Console.WriteLine("PASS " + name); passed++; }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); failed++; }
    }
    void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected exception: " + typeof(T).Name);
    }
    foreach (var scenario in new[] {
        (PlaylistJournalState.Prepared, PlaylistRecoveryDisposition.StagingCleanupReview),
        (PlaylistJournalState.CommitStarted, PlaylistRecoveryDisposition.LocalCommitReview),
        (PlaylistJournalState.VerificationPending, PlaylistRecoveryDisposition.RemoteVerificationRequired),
        (PlaylistJournalState.RecoveryRequired, PlaylistRecoveryDisposition.ManualInterventionRequired),
        (PlaylistJournalState.Verified, PlaylistRecoveryDisposition.NoActionRequired),
        (PlaylistJournalState.Recovered, PlaylistRecoveryDisposition.NoActionRequired)
    })
    {
        Test("Classifies " + scenario.Item1, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var journal = new PlaylistTransactionJournalService(d);
            var id = Guid.NewGuid();
            journal.Write(new(id, "test.m3u", scenario.Item1, DateTimeOffset.UtcNow, "controlled test"));
            var assessments = new PlaylistRecoveryCoordinator(journal).Assess();
            Check(assessments.Count == 1 && assessments[0].Disposition == scenario.Item2, "Incorrect disposition");
            Check(assessments[0].TransactionId == id, "Identity changed");
        });
    }
    Test("Empty journal", () => {
        var journal = new PlaylistTransactionJournalService(Path.Combine(root, "missing"));
        Check(new PlaylistRecoveryCoordinator(journal).Assess().Count == 0, "Unexpected records");
    });
    Test("Idempotent read-only assessment", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var journal = new PlaylistTransactionJournalService(d);
        journal.Write(new(Guid.NewGuid(), "sample.m3u", PlaylistJournalState.CommitStarted, DateTimeOffset.UtcNow, "test"));
        var before = Directory.GetFiles(d).ToDictionary(x => Path.GetFileName(x), File.ReadAllBytes);
        var coordinator = new PlaylistRecoveryCoordinator(journal);
        var first = coordinator.Assess(); var second = coordinator.Assess();
        Check(first.SequenceEqual(second), "Assessments differ");
        Check(before.Count == Directory.GetFiles(d).Length, "File count changed");
        foreach (var kv in before) Check(kv.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(d, kv.Key))), "Journal mutated");
    });
    Test("Corrupt journal fails closed", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "bad.json"), "{not json");
        Throws<JsonException>(() => new PlaylistRecoveryCoordinator(new(d)).Assess());
    });
    Test("Invalid transaction ID fails closed", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "bad.json"), JsonSerializer.Serialize(new PlaylistJournalEntry(Guid.Empty,"sample.m3u",PlaylistJournalState.Prepared,DateTimeOffset.UtcNow,"")));
        Throws<InvalidOperationException>(() => new PlaylistRecoveryCoordinator(new(d)).Assess());
    });
    Test("Unknown state fails closed", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "bad.json"), JsonSerializer.Serialize(new PlaylistJournalEntry(Guid.NewGuid(),"sample.m3u",(PlaylistJournalState)999,DateTimeOffset.UtcNow,"")));
        Throws<InvalidOperationException>(() => new PlaylistRecoveryCoordinator(new(d)).Assess());
    });
    Test("Duplicate transaction IDs fail closed", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        var entry = new PlaylistJournalEntry(Guid.NewGuid(),"sample.m3u",PlaylistJournalState.Prepared,DateTimeOffset.UtcNow,"");
        File.WriteAllText(Path.Combine(d,"a.json"),JsonSerializer.Serialize(entry));
        File.WriteAllText(Path.Combine(d,"b.json"),JsonSerializer.Serialize(entry));
        Throws<InvalidOperationException>(() => new PlaylistRecoveryCoordinator(new(d)).Assess());
    });
    Test("Incomplete state filtering", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var journal = new PlaylistTransactionJournalService(d);
        journal.Write(new(Guid.NewGuid(),"a.m3u",PlaylistJournalState.Prepared,DateTimeOffset.UtcNow,""));
        journal.Write(new(Guid.NewGuid(),"b.m3u",PlaylistJournalState.Verified,DateTimeOffset.UtcNow,""));
        journal.Write(new(Guid.NewGuid(),"c.m3u",PlaylistJournalState.Recovered,DateTimeOffset.UtcNow,""));
        Check(journal.FindIncomplete().Count == 1, "Incorrect incomplete count");
    });
    // Alpha.42 tests operate ONLY under a newly created OS temp directory.
    void SandboxTest(string name, Action<string, IsolatedPlaylistRollbackService, IsolatedRollbackEvidence, string> body)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(d, "backups"));
            Directory.CreateDirectory(Path.Combine(d, "playlists"));
            var id = Guid.NewGuid();
            string target = Path.Combine(d, "playlists", "test.m3u");
            File.WriteAllText(Path.Combine(d, "backups", id.ToString("N") + ".m3u"), "original\n");
            File.WriteAllText(target, "committed\n");
            var evidence = new IsolatedRollbackEvidence(id, "test.m3u",
                IsolatedPlaylistRollbackService.Fingerprint(System.Text.Encoding.UTF8.GetBytes("original\n")),
                IsolatedPlaylistRollbackService.Fingerprint(System.Text.Encoding.UTF8.GetBytes("committed\n")));
            body(d, new IsolatedPlaylistRollbackService(d), evidence, target);
        });
    }
    SandboxTest("Sandbox rollback restores original", (d, service, e, target) => {
        Check(service.Restore(e) == IsolatedRollbackResult.Restored, "Not restored");
        Check(File.ReadAllText(target) == "original\n", "Wrong contents");
    });
    SandboxTest("Repeated rollback is idempotent", (d, service, e, target) => {
        service.Restore(e);
        Check(service.Restore(e) == IsolatedRollbackResult.AlreadyRestored, "Not idempotent");
    });
    SandboxTest("Tampered backup fails closed", (d, service, e, target) => {
        File.WriteAllText(Path.Combine(d,"backups",e.TransactionId.ToString("N")+".m3u"), "tampered");
        Throws<InvalidDataException>(() => service.Restore(e));
        Check(File.ReadAllText(target) == "committed\n", "Target changed");
    });
    SandboxTest("Unexpected destination change fails closed", (d, service, e, target) => {
        File.WriteAllText(target,"external change");
        Throws<InvalidDataException>(() => service.Restore(e));
        Check(File.ReadAllText(target) == "external change", "Target changed");
    });
    SandboxTest("Missing backup fails closed", (d, service, e, target) => {
        File.Delete(Path.Combine(d,"backups",e.TransactionId.ToString("N")+".m3u"));
        Throws<InvalidOperationException>(() => service.Restore(e));
    });
    SandboxTest("Unsafe playlist path fails closed", (d, service, e, target) => {
        Throws<InvalidDataException>(() => service.Restore(e with { PlaylistFileName = "../test.m3u" }));
        Check(File.ReadAllText(target) == "committed\n", "Target changed");
    });
    SandboxTest("Invalid fingerprint fails closed", (d, service, e, target) => {
        Throws<InvalidDataException>(() => service.Restore(e with { OriginalSha256 = "invalid" }));
    });


    // Alpha.43: isolated journal integration; never targets production paths.
    void JournalTest(string name, Action<string, PlaylistTransactionJournalService,
        IsolatedJournalRecoveryCoordinator, IsolatedRollbackEvidence, string> body)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(d, "backups"));
            Directory.CreateDirectory(Path.Combine(d, "playlists"));
            var id = Guid.NewGuid();
            string target = Path.Combine(d, "playlists", "test.m3u");
            File.WriteAllText(Path.Combine(d, "backups", id.ToString("N") + ".m3u"), "original\n");
            File.WriteAllText(target, "committed\n");
            var evidence = new IsolatedRollbackEvidence(id, "test.m3u",
                IsolatedPlaylistRollbackService.Fingerprint(System.Text.Encoding.UTF8.GetBytes("original\n")),
                IsolatedPlaylistRollbackService.Fingerprint(System.Text.Encoding.UTF8.GetBytes("committed\n")));
            var journal = new PlaylistTransactionJournalService(Path.Combine(d, "journal"));
            body(d, journal, new IsolatedJournalRecoveryCoordinator(journal,
                new IsolatedPlaylistRollbackService(d)), evidence, target);
        });
    }
    JournalTest("Journal rollback transitions to Recovered", (d,j,c,e,target) => {
        j.Write(new(e.TransactionId,e.PlaylistFileName,PlaylistJournalState.CommitStarted,DateTimeOffset.UtcNow,""));
        Check(c.Recover(e) == IsolatedRollbackResult.Restored,"Not restored");
        Check(j.ReadAll().Single().State == PlaylistJournalState.Recovered,"Not recorded");
        Check(File.ReadAllText(target) == "original\n","Incorrect contents");
    });
    JournalTest("Journal failure preserves recovery intent", (d,j,c,e,target) => {
        j.Write(new(e.TransactionId,e.PlaylistFileName,PlaylistJournalState.CommitStarted,DateTimeOffset.UtcNow,""));
        File.WriteAllText(target,"unexpected");
        Throws<InvalidDataException>(() => c.Recover(e));
        Check(j.ReadAll().Single().State == PlaylistJournalState.RecoveryRequired,"Lost intent");
        Check(File.ReadAllText(target) == "unexpected","Unexpected overwrite");
    });
    JournalTest("Journal retry after interrupted file restore", (d,j,c,e,target) => {
        j.Write(new(e.TransactionId,e.PlaylistFileName,PlaylistJournalState.CommitStarted,DateTimeOffset.UtcNow,""));
        new IsolatedPlaylistRollbackService(d).Restore(e);
        Check(c.Recover(e) == IsolatedRollbackResult.AlreadyRestored,"Retry failed");
        Check(j.ReadAll().Single().State == PlaylistJournalState.Recovered,"Journal not finalized");
    });
    JournalTest("Remote verification pending refuses rollback", (d,j,c,e,target) => {
        j.Write(new(e.TransactionId,e.PlaylistFileName,PlaylistJournalState.VerificationPending,DateTimeOffset.UtcNow,""));
        Throws<InvalidOperationException>(() => c.Recover(e));
        Check(File.ReadAllText(target) == "committed\n","Unexpected mutation");
    });
    JournalTest("Unproven recovery intent refuses rollback", (d,j,c,e,target) => {
        j.Write(new(e.TransactionId,e.PlaylistFileName,PlaylistJournalState.RecoveryRequired,DateTimeOffset.UtcNow,"unknown"));
        Throws<InvalidOperationException>(() => c.Recover(e));
    });
    JournalTest("Mismatched playlist identity refuses rollback", (d,j,c,e,target) => {
        j.Write(new(e.TransactionId,"other.m3u",PlaylistJournalState.CommitStarted,DateTimeOffset.UtcNow,""));
        Throws<InvalidDataException>(() => c.Recover(e));
    });
    JournalTest("Missing transaction refuses rollback", (d,j,c,e,target) => {
        Throws<InvalidDataException>(() => c.Recover(e));
    });


    // Alpha.44: immutable evidence records are isolated; no production journal integration.
    void EvidenceTest(string name, Action<string, IsolatedRecoveryEvidenceStore, IsolatedRecoveryEvidenceRecord> body)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            var store = new IsolatedRecoveryEvidenceStore(d);
            var record = new IsolatedRecoveryEvidenceRecord(Guid.NewGuid(), "test.m3u",
                new string('A',64), new string('B',64), "IntentRecorded", DateTimeOffset.UtcNow);
            body(d,store,record);
        });
    }
    EvidenceTest("Evidence append and read verifies digest", (d,store,e) => {
        var path=store.Append(e);
        Check(store.Read(path)==e,"Evidence roundtrip mismatch");
    });
    EvidenceTest("Evidence append preserves prior records", (d,store,e) => {
        var first=store.Append(e);
        var second=store.Append(e with {Phase="RestoreVerified"});
        Check(first!=second && File.Exists(first) && File.Exists(second),"Evidence overwritten");
        Check(store.Read(first).Phase=="IntentRecorded","Prior evidence changed");
    });
    EvidenceTest("Tampered evidence fails closed", (d,store,e) => {
        var path=store.Append(e);
        File.WriteAllText(path,File.ReadAllText(path).Replace("IntentRecorded","RestoreVerified"));
        Throws<InvalidDataException>(()=>store.Read(path));
    });
    EvidenceTest("Evidence path traversal fails closed", (d,store,e) => {
        var path=store.Append(e);
        Throws<InvalidDataException>(()=>store.Read(Path.Combine(d,"..",Path.GetFileName(path))));
    });
    EvidenceTest("Invalid evidence phase fails closed", (d,store,e) => {
        Throws<InvalidDataException>(()=>store.Append(e with {Phase="Unknown"}));
    });


    // Alpha.45: read-only restart assessments in fresh temporary directories.
    void RestartTest(string name, Action<PlaylistTransactionJournalService, IsolatedRecoveryEvidenceStore,
        IsolatedRestartReconciliationService, Guid> body)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            string evidenceDir = Path.Combine(d, "evidence");
            Directory.CreateDirectory(evidenceDir);
            var journal = new PlaylistTransactionJournalService(Path.Combine(d, "journal"));
            var store = new IsolatedRecoveryEvidenceStore(evidenceDir);
            var service = new IsolatedRestartReconciliationService(journal, store, evidenceDir);
            body(journal, store, service, Guid.NewGuid());
        });
    }
    IsolatedRecoveryEvidenceRecord Record(Guid id, string phase) =>
        new(id, "test.m3u", new string('A',64), new string('B',64), phase, DateTimeOffset.UtcNow);
    RestartTest("Restart without evidence remains read-only", (j,e,r,id) => {
        j.Write(new(id,"test.m3u",PlaylistJournalState.CommitStarted,DateTimeOffset.UtcNow,""));
        Check(r.Assess().Single().Status == RestartEvidenceStatus.NoEvidence, "Wrong state");
    });
    RestartTest("Restart with intent requires review", (j,e,r,id) => {
        j.Write(new(id,"test.m3u",PlaylistJournalState.RecoveryRequired,DateTimeOffset.UtcNow,""));
        e.Append(Record(id,"IntentRecorded"));
        Check(r.Assess().Single().Status == RestartEvidenceStatus.IntentRecorded, "Wrong state");
    });
    RestartTest("Restored file with unfinished journal requires review", (j,e,r,id) => {
        j.Write(new(id,"test.m3u",PlaylistJournalState.RecoveryRequired,DateTimeOffset.UtcNow,""));
        e.Append(Record(id,"IntentRecorded")); e.Append(Record(id,"RestoreVerified"));
        Check(r.Assess().Single().Status == RestartEvidenceStatus.RestoreVerifiedNeedsJournalReview, "Wrong state");
    });
    RestartTest("Completed recovery evidence recognized", (j,e,r,id) => {
        j.Write(new(id,"test.m3u",PlaylistJournalState.Recovered,DateTimeOffset.UtcNow,""));
        e.Append(Record(id,"RestoreVerified"));
        Check(r.Assess().Single().Status == RestartEvidenceStatus.CompletedWithEvidence, "Wrong state");
    });
    RestartTest("Orphan evidence fails closed", (j,e,r,id) => {
        e.Append(Record(id,"IntentRecorded"));
        Throws<InvalidDataException>(() => r.Assess());
    });
    RestartTest("Conflicting fingerprints require review", (j,e,r,id) => {
        j.Write(new(id,"test.m3u",PlaylistJournalState.RecoveryRequired,DateTimeOffset.UtcNow,""));
        e.Append(Record(id,"IntentRecorded"));
        e.Append(Record(id,"RestoreVerified") with { OriginalSha256 = new string('C',64) });
        Check(r.Assess().Single().Status == RestartEvidenceStatus.InconsistentEvidence, "Conflict not detected");
    });
    // Deliberately hold a destination handle open to exercise Windows sharing behavior.
    // On non-Windows hosts, replacement semantics differ; this test is Windows-only.
    Test("Journal lock contention preserves prior record", () => {
        if (!OperatingSystem.IsWindows()) return;
        string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var j = new PlaylistTransactionJournalService(d);
        var id = Guid.NewGuid();
        j.Write(new(id,"test.m3u",PlaylistJournalState.Prepared,DateTimeOffset.UtcNow,"initial"));
        string path = Path.Combine(d,id.ToString("N")+".json");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool blocked = false;
            try
            {
                j.Write(new(id,"test.m3u",PlaylistJournalState.RecoveryRequired,DateTimeOffset.UtcNow,"retry"));
            }
            catch (IOException) { blocked = true; }
            catch (UnauthorizedAccessException) { blocked = true; }
            Check(blocked, "Journal replacement unexpectedly succeeded while destination was locked");
            Check(j.ReadAll().Single().State == PlaylistJournalState.Prepared, "Existing record was damaged");
        }
        j.Write(new(id,"test.m3u",PlaylistJournalState.RecoveryRequired,DateTimeOffset.UtcNow,"retry"));
        Check(j.ReadAll().Single().State == PlaylistJournalState.RecoveryRequired, "Retry failed");
    });

    // Alpha.46: read-only safety audit in a fresh sandbox, no playlist mutations.
    void AuditTest(string name, PlaylistJournalState state, string[] phases,
        RestartSafetyFinding expected, bool reversed = false)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            string evidenceDir = Path.Combine(d, "evidence");
            Directory.CreateDirectory(evidenceDir);
            var journal = new PlaylistTransactionJournalService(Path.Combine(d, "journal"));
            var store = new IsolatedRecoveryEvidenceStore(evidenceDir);
            var id = Guid.NewGuid();
            var time = DateTimeOffset.UtcNow;
            journal.Write(new(id, "test.m3u", state, time.AddMinutes(5), "audit"));
            for (int i = 0; i < phases.Length; i++)
                store.Append(new(id, "test.m3u", new string('A',64), new string('B',64),
                    phases[i], time.AddMinutes(reversed ? phases.Length - i : i)));
            var audit = new IsolatedRestartSafetyAuditService(journal, store, evidenceDir);
            Check(audit.Assess().Single().Finding == expected, "Incorrect safety finding");
            Check(audit.Assess().Single().Finding == expected, "Read-only audit not repeatable");
        });
    }
    AuditTest("Recovered journal without restoration evidence requires review",
        PlaylistJournalState.Recovered, Array.Empty<string>(),
        RestartSafetyFinding.RecoveredWithoutRestoreEvidence);
    AuditTest("Restoration evidence without intent requires review",
        PlaylistJournalState.Recovered, new[] { "RestoreVerified" },
        RestartSafetyFinding.RestoreEvidenceWithoutIntent);
    AuditTest("Pending remote verification remains unresolved",
        PlaylistJournalState.VerificationPending, Array.Empty<string>(),
        RestartSafetyFinding.RemoteVerificationUnresolved);
    AuditTest("Out-of-order evidence timestamps require review",
        PlaylistJournalState.Recovered, new[] { "IntentRecorded", "RestoreVerified" },
        RestartSafetyFinding.NonMonotonicEvidenceTimestamps, reversed: true);
    AuditTest("Consistent recovery evidence passes read-only audit",
        PlaylistJournalState.Recovered, new[] { "IntentRecorded", "RestoreVerified" },
        RestartSafetyFinding.None);


    // Alpha.47: isolated sequence-chain checks; timestamps are deliberately not used for ordering.
    void SequenceTest(string name, Action<string, IsolatedTransactionSequenceStore, Guid> body)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            body(d, new IsolatedTransactionSequenceStore(d), Guid.NewGuid());
        });
    }
    SequenceTest("Sequence append verifies consecutive events", (d,s,id) => {
        s.Append(id,"test.m3u","IntentRecorded",DateTimeOffset.UtcNow);
        s.Append(id,"test.m3u","RestoreVerified",DateTimeOffset.UtcNow);
        var events = s.ReadVerified(id);
        Check(events.Count == 2 && events[0].Sequence == 1 && events[1].Sequence == 2, "Sequence incorrect");
    });
    SequenceTest("Sequence ignores clock reversal", (d,s,id) => {
        var now = DateTimeOffset.UtcNow;
        s.Append(id,"test.m3u","IntentRecorded",now);
        s.Append(id,"test.m3u","RestoreVerified",now.AddDays(-1));
        Check(s.ReadVerified(id).Last().Sequence == 2, "Clock affected sequence ordering");
    });
    SequenceTest("Sequence missing predecessor fails closed", (d,s,id) => {
        string first=s.Append(id,"test.m3u","IntentRecorded",DateTimeOffset.UtcNow);
        s.Append(id,"test.m3u","RestoreVerified",DateTimeOffset.UtcNow);
        File.Delete(first);
        Throws<InvalidDataException>(()=>s.ReadVerified(id));
    });
    SequenceTest("Sequence tampering fails closed", (d,s,id) => {
        string path=s.Append(id,"test.m3u","IntentRecorded",DateTimeOffset.UtcNow);
        File.WriteAllText(path,File.ReadAllText(path).Replace("IntentRecorded","RecoveryFailed"));
        Throws<InvalidDataException>(()=>s.ReadVerified(id));
    });
    SequenceTest("Sequence identity mismatch fails closed", (d,s,id) => {
        s.Append(id,"test.m3u","IntentRecorded",DateTimeOffset.UtcNow);
        Throws<InvalidDataException>(()=>s.Append(id,"other.m3u","RestoreVerified",DateTimeOffset.UtcNow));
        Check(s.ReadVerified(id).Count==1,"Unexpected event appended");
    });
    SequenceTest("Sequence existing record cannot be overwritten", (d,s,id) => {
        string path=s.Append(id,"test.m3u","IntentRecorded",DateTimeOffset.UtcNow);
        var original=File.ReadAllBytes(path);
        Throws<IOException>(()=> {
            using var stream = new FileStream(path,FileMode.CreateNew,FileAccess.Write);
        });
        Check(File.ReadAllBytes(path).SequenceEqual(original),"Existing event changed");
    });


    // Alpha.48: read-only cross-source audit; all artifacts are sandbox-only.
    void CrossAuditTest(string name, PlaylistJournalState state, string[] seqPhases,
        string[] evidencePhases, JournalSequenceFinding expected, bool mismatch = false)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            string eDir = Path.Combine(d, "evidence"), sDir = Path.Combine(d, "sequence");
            Directory.CreateDirectory(eDir); Directory.CreateDirectory(sDir);
            var j = new PlaylistTransactionJournalService(Path.Combine(d, "journal"));
            var e = new IsolatedRecoveryEvidenceStore(eDir);
            var s = new IsolatedTransactionSequenceStore(sDir);
            Guid id = Guid.NewGuid();
            j.Write(new(id, "test.m3u", state, DateTimeOffset.UtcNow, "audit"));
            foreach (string phase in seqPhases)
                s.Append(id, mismatch ? "other.m3u" : "test.m3u", phase, DateTimeOffset.UtcNow);
            foreach (string phase in evidencePhases)
                e.Append(new(id, "test.m3u", new string('A',64), new string('B',64), phase, DateTimeOffset.UtcNow));
            var audit = new IsolatedJournalSequenceAuditService(j,e,eDir,s,sDir);
            Check(audit.Assess().Single().Finding == expected, "Unexpected cross-audit finding");
            Check(audit.Assess().Single().Finding == expected, "Audit was not repeatable");
        });
    }
    CrossAuditTest("Cross audit consistent recovered transaction", PlaylistJournalState.Recovered,
        new[] { "IntentRecorded", "RestoreVerified" }, new[] { "IntentRecorded", "RestoreVerified" },
        JournalSequenceFinding.Consistent);
    CrossAuditTest("Cross audit missing sequence requires review", PlaylistJournalState.Recovered,
        Array.Empty<string>(), new[] { "IntentRecorded", "RestoreVerified" }, JournalSequenceFinding.MissingSequence);
    CrossAuditTest("Cross audit missing restore evidence requires review", PlaylistJournalState.Recovered,
        new[] { "IntentRecorded", "RestoreVerified" }, new[] { "IntentRecorded" },
        JournalSequenceFinding.MissingRestoreEvidence);
    CrossAuditTest("Cross audit mismatched playlist identity requires review", PlaylistJournalState.Recovered,
        new[] { "IntentRecorded", "RestoreVerified" }, new[] { "IntentRecorded", "RestoreVerified" },
        JournalSequenceFinding.ConflictingIdentity, mismatch: true);
    CrossAuditTest("Cross audit remote verification remains unresolved", PlaylistJournalState.VerificationPending,
        new[] { "IntentRecorded" }, new[] { "IntentRecorded" },
        JournalSequenceFinding.UnresolvedRemoteVerification);
    CrossAuditTest("Cross audit unfinished journal requires review", PlaylistJournalState.RecoveryRequired,
        new[] { "IntentRecorded" }, new[] { "IntentRecorded" }, JournalSequenceFinding.UnfinishedRecovery);


    // Alpha.49: decisions are diagnostic only; none permits playlist or Zidoo changes.
    void AuthorizationTest(string name, PlaylistJournalState state, string[] sequencePhases,
        string[] evidencePhases, RecoveryAuthorizationDisposition expected, bool mismatch = false)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            string jDir = Path.Combine(d,"journal"), eDir = Path.Combine(d,"evidence"), sDir = Path.Combine(d,"sequence");
            Directory.CreateDirectory(jDir); Directory.CreateDirectory(eDir); Directory.CreateDirectory(sDir);
            var id = Guid.NewGuid();
            var journal = new PlaylistTransactionJournalService(jDir);
            var evidence = new IsolatedRecoveryEvidenceStore(eDir);
            var sequence = new IsolatedTransactionSequenceStore(sDir);
            journal.Write(new(id,"test.m3u",state,DateTimeOffset.UtcNow,"test"));
            foreach (var phase in sequencePhases)
                sequence.Append(id,mismatch ? "other.m3u" : "test.m3u",phase,DateTimeOffset.UtcNow);
            foreach (var phase in evidencePhases)
                evidence.Append(new(id,"test.m3u",new string('A',64),new string('B',64),phase,DateTimeOffset.UtcNow));
            var audit = new IsolatedJournalSequenceAuditService(journal,evidence,eDir,sequence,sDir);
            var gate = new IsolatedRecoveryAuthorizationService(audit);
            var first = gate.Assess().Single();
            Check(first.TransactionId == id && first.Disposition == expected, "Incorrect disposition");
            Check(!first.MayMutate, "Read-only gate permitted mutation");
            Check(first == gate.Assess().Single(), "Non-idempotent decision");
        });
    }
    AuthorizationTest("Authorization consistent history is diagnostic only", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded","RestoreVerified"},
        RecoveryAuthorizationDisposition.HistoricalConsistencyOnly);
    AuthorizationTest("Authorization missing sequence denies", PlaylistJournalState.Recovered,
        Array.Empty<string>(), new[] {"IntentRecorded","RestoreVerified"},
        RecoveryAuthorizationDisposition.DeniedMissingOrConflictingEvidence);
    AuthorizationTest("Authorization missing restoration evidence denies", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded"},
        RecoveryAuthorizationDisposition.DeniedMissingOrConflictingEvidence);
    AuthorizationTest("Authorization identity conflict denies", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded","RestoreVerified"},
        RecoveryAuthorizationDisposition.DeniedMissingOrConflictingEvidence, mismatch: true);
    AuthorizationTest("Authorization remote verification pending denies", PlaylistJournalState.VerificationPending,
        new[] {"IntentRecorded"}, new[] {"IntentRecorded"},
        RecoveryAuthorizationDisposition.DeniedRemoteVerificationPending);
    AuthorizationTest("Authorization unfinished recovery denies", PlaylistJournalState.RecoveryRequired,
        new[] {"IntentRecorded"}, new[] {"IntentRecorded"},
        RecoveryAuthorizationDisposition.DeniedIncompleteRecovery);


    // Alpha.50: execution plans remain read-only regardless of the evidence finding.
    void ExecutionPlanTest(string name, PlaylistJournalState state, string[] sequencePhases,
        string[] evidencePhases, RecoveryPlanDisposition expected, bool mismatch = false)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            string jDir = Path.Combine(d,"journal"), eDir = Path.Combine(d,"evidence"), sDir = Path.Combine(d,"sequence");
            Directory.CreateDirectory(jDir); Directory.CreateDirectory(eDir); Directory.CreateDirectory(sDir);
            Guid id = Guid.NewGuid();
            var journal = new PlaylistTransactionJournalService(jDir);
            var evidence = new IsolatedRecoveryEvidenceStore(eDir);
            var sequence = new IsolatedTransactionSequenceStore(sDir);
            journal.Write(new(id,"test.m3u",state,DateTimeOffset.UtcNow,"test"));
            foreach (string phase in sequencePhases)
                sequence.Append(id,mismatch ? "other.m3u" : "test.m3u",phase,DateTimeOffset.UtcNow);
            foreach (string phase in evidencePhases)
                evidence.Append(new(id,"test.m3u",new string('A',64),new string('B',64),phase,DateTimeOffset.UtcNow));
            var audit = new IsolatedJournalSequenceAuditService(journal,evidence,eDir,sequence,sDir);
            var authorization = new IsolatedRecoveryAuthorizationService(audit);
            var planner = new IsolatedRecoveryExecutionPlanner(authorization);
            var plan = planner.Assess().Single();
            Check(plan.TransactionId == id && plan.Disposition == expected, "Unexpected execution plan");
            Check(!plan.MayExecute, "Planner permitted mutation");
            Check(plan == planner.Assess().Single(), "Plan not repeatable");
        });
    }
    ExecutionPlanTest("Plan completed history requires no action", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded","RestoreVerified"},
        RecoveryPlanDisposition.CompletedHistoryNoAction);
    ExecutionPlanTest("Plan missing sequence requires manual review", PlaylistJournalState.Recovered,
        Array.Empty<string>(), new[] {"IntentRecorded","RestoreVerified"},
        RecoveryPlanDisposition.ManualReviewRequired);
    ExecutionPlanTest("Plan missing restore evidence requires manual review", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded"},
        RecoveryPlanDisposition.ManualReviewRequired);
    ExecutionPlanTest("Plan identity conflict requires manual review", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded","RestoreVerified"},
        RecoveryPlanDisposition.ManualReviewRequired, mismatch: true);
    ExecutionPlanTest("Plan remote verification requires additional verification", PlaylistJournalState.VerificationPending,
        new[] {"IntentRecorded"}, new[] {"IntentRecorded"},
        RecoveryPlanDisposition.RemoteVerificationRequired);
    ExecutionPlanTest("Plan unfinished recovery requires manual review", PlaylistJournalState.RecoveryRequired,
        new[] {"IntentRecorded"}, new[] {"IntentRecorded"},
        RecoveryPlanDisposition.RecoveryIncompleteReviewRequired);


    // Alpha.51: immutable diagnostic report persistence, never execution authorization.
    void PlanReportTest(string name, Action<IsolatedRecoveryPlanReportStore, RecoveryExecutionPlan, string> body)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            var store = new IsolatedRecoveryPlanReportStore(d);
            var plan = new RecoveryExecutionPlan(Guid.NewGuid(), RecoveryPlanDisposition.ManualReviewRequired,
                RecoveryAuthorizationDisposition.DeniedMissingOrConflictingEvidence,
                JournalSequenceFinding.MissingSequence);
            body(store, plan, d);
        });
    }
    PlanReportTest("Report append and read preserves diagnostic decision", (store, plan, d) => {
        var path = store.Append(plan, DateTimeOffset.UtcNow);
        var read = store.Read(path);
        Check(read.TransactionId == plan.TransactionId && read.Disposition == plan.Disposition &&
              read.Authorization == plan.Authorization && read.Finding == plan.Finding && !read.MayExecute,
              "Report contents differ or permit execution");
    });
    PlanReportTest("Report append preserves earlier record", (store, plan, d) => {
        var first = store.Append(plan, DateTimeOffset.UtcNow);
        var second = store.Append(plan, DateTimeOffset.UtcNow);
        Check(first != second && File.Exists(first) && File.Exists(second) &&
              store.Read(first).TransactionId == plan.TransactionId, "Report overwritten");
    });
    PlanReportTest("Report tampering fails closed", (store, plan, d) => {
        var path = store.Append(plan, DateTimeOffset.UtcNow);
        // Test fixture tampering must tolerate transient sharing violations from
        // antivirus/indexing; persistent locks still fail the test.
        for (int attempt = 0; ; attempt++)
        {
            try { File.AppendAllText(path, "corrupt"); break; }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33 && attempt < 6)
            { Thread.Sleep(40 * (attempt + 1)); }
        }
        Throws<Exception>(() => store.Read(path));
    });
    PlanReportTest("Report path traversal fails closed", (store, plan, d) => {
        Throws<InvalidDataException>(() => store.Read(Path.Combine(d, "..", "outside.json")));
    });
    PlanReportTest("Report missing file fails closed", (store, plan, d) => {
        Throws<FileNotFoundException>(() => store.Read(Path.Combine(d,
            Guid.NewGuid().ToString("N") + "-" + Guid.NewGuid().ToString("N") + ".json")));
    });
    PlanReportTest("Report remains non-executable after restart", (store, plan, d) => {
        var path = store.Append(plan, DateTimeOffset.UtcNow);
        var restarted = new IsolatedRecoveryPlanReportStore(d);
        Check(!restarted.Read(path).MayExecute && !plan.MayExecute, "Persisted plan authorized execution");
    });


    // Alpha.52: diagnostic aggregation over the existing read-only plan chain.
    void DiagnosticsTest(string name, PlaylistJournalState state, string[] seqPhases,
        string[] evidencePhases, RecoveryDiagnosticStatus expected, bool mismatch = false)
    {
        Test(name, () => {
            string d = Path.Combine(root, Guid.NewGuid().ToString("N"));
            string jDir = Path.Combine(d, "journal"), eDir = Path.Combine(d, "evidence"), sDir = Path.Combine(d, "sequence");
            Directory.CreateDirectory(jDir); Directory.CreateDirectory(eDir); Directory.CreateDirectory(sDir);
            Guid id = Guid.NewGuid();
            var journal = new PlaylistTransactionJournalService(jDir);
            var evidence = new IsolatedRecoveryEvidenceStore(eDir);
            var sequence = new IsolatedTransactionSequenceStore(sDir);
            journal.Write(new(id, "test.m3u", state, DateTimeOffset.UtcNow, "test"));
            foreach (var phase in seqPhases)
                sequence.Append(id, mismatch ? "other.m3u" : "test.m3u", phase, DateTimeOffset.UtcNow);
            foreach (var phase in evidencePhases)
                evidence.Append(new(id, "test.m3u", new string('A',64), new string('B',64), phase, DateTimeOffset.UtcNow));
            var audit = new IsolatedJournalSequenceAuditService(journal, evidence, eDir, sequence, sDir);
            var planner = new IsolatedRecoveryExecutionPlanner(new IsolatedRecoveryAuthorizationService(audit));
            var diagnostics = new IsolatedRecoveryDiagnosticsService(planner);
            var summary = diagnostics.Assess();
            Check(summary.Total == 1 && summary.Items.Count == 1 && summary.Items[0].TransactionId == id &&
                  summary.Items[0].Status == expected && !summary.MayExecute && !summary.Items[0].MayExecute,
                  "Unexpected or executable diagnostics");
            Check(summary.Completed + summary.RemoteVerificationPending + summary.RecoveryIncomplete +
                  summary.ManualReviewRequired == summary.Total, "Diagnostic totals disagree");
            Check(summary == diagnostics.Assess() ||
                  summary.Items.SequenceEqual(diagnostics.Assess().Items), "Diagnostics not repeatable");
        });
    }
    DiagnosticsTest("Diagnostics consistent history is completed", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded","RestoreVerified"},
        RecoveryDiagnosticStatus.Completed);
    DiagnosticsTest("Diagnostics missing sequence requires review", PlaylistJournalState.Recovered,
        Array.Empty<string>(), new[] {"IntentRecorded","RestoreVerified"},
        RecoveryDiagnosticStatus.ManualReviewRequired);
    DiagnosticsTest("Diagnostics missing evidence requires review", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded"},
        RecoveryDiagnosticStatus.ManualReviewRequired);
    DiagnosticsTest("Diagnostics identity mismatch requires review", PlaylistJournalState.Recovered,
        new[] {"IntentRecorded","RestoreVerified"}, new[] {"IntentRecorded","RestoreVerified"},
        RecoveryDiagnosticStatus.ManualReviewRequired, mismatch: true);
    DiagnosticsTest("Diagnostics remote verification stays pending", PlaylistJournalState.VerificationPending,
        new[] {"IntentRecorded"}, new[] {"IntentRecorded"},
        RecoveryDiagnosticStatus.RemoteVerificationPending);
    DiagnosticsTest("Diagnostics incomplete recovery requires review", PlaylistJournalState.RecoveryRequired,
        new[] {"IntentRecorded"}, new[] {"IntentRecorded"},
        RecoveryDiagnosticStatus.RecoveryIncomplete);

    // Alpha.53: report browsing is read-only, including invalid-file handling.
    Test("Browser missing directory returns empty", () => {
        Check(new IsolatedRecoveryReportBrowserService().List(Path.Combine(root, "no-reports")).Count == 0, "Expected empty results");
    });
    Test("Browser empty directory returns empty", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        Check(new IsolatedRecoveryReportBrowserService().List(d).Count == 0, "Expected empty results");
    });
    Test("Browser corrupt report requires review", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, Guid.NewGuid().ToString("N") + ".json"), "not-json");
        var rows = new IsolatedRecoveryReportBrowserService().List(d);
        Check(rows.Count == 1 && rows[0].Status == "Manual review required" && !rows[0].MayExecute, "Corruption not reported");
    });
    Test("Browser leaves report bytes unchanged", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        string p = Path.Combine(d, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(p, "invalid");
        var before = File.ReadAllBytes(p); new IsolatedRecoveryReportBrowserService().List(d);
        Check(before.SequenceEqual(File.ReadAllBytes(p)), "Browser modified report");
    });
    Test("Browser ignores unrelated extensions", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "notes.txt"), "unrelated");
        Check(new IsolatedRecoveryReportBrowserService().List(d).Count == 0, "Unrelated file listed");
    });
    Test("Browser rejects redirected report root", () => {
        string d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        string link = Path.Combine(root, Guid.NewGuid().ToString("N"));
        try { Directory.CreateSymbolicLink(link, d); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        Throws<InvalidDataException>(() => new IsolatedRecoveryReportBrowserService().List(link));
    });

    // Alpha.54: read-only UI summary tests, independent of WPF and filesystem.
    Test("UI summary empty report list", () => {
        var summary = RecoveryReportDisplaySummary.From(Array.Empty<RecoveryReportRow>());
        Check(summary.Total == 0 && summary.NeedsReview == 0 && summary.Pending == 0 && summary.Other == 0, "Unexpected empty summary");
    });
    Test("UI summary flags malformed report for review", () => {
        var row = new RecoveryReportRow(default, Guid.Empty, "Manual review required", "Invalid recovery report filename", "invalid.json");
        var summary = RecoveryReportDisplaySummary.From(new[] { row });
        Check(summary.Total == 1 && summary.NeedsReview == 1 && summary.Pending == 0 && !row.MayExecute, "Review was not counted");
    });
    Test("UI summary distinguishes pending verification", () => {
        var row = new RecoveryReportRow(default, Guid.NewGuid(), "Remote verification pending", "Pending", "pending.json");
        var summary = RecoveryReportDisplaySummary.From(new[] { row });
        Check(summary.Total == 1 && summary.Pending == 1 && summary.NeedsReview == 0, "Pending was not counted");
    });
    Test("UI summary preserves report rows", () => {
        var rows = new[] {
            new RecoveryReportRow(default, Guid.NewGuid(), "Manual review required", "Mismatch", "one.json"),
            new RecoveryReportRow(default, Guid.NewGuid(), "Completed", "None", "two.json")
        };
        var snapshot = rows.ToArray();
        var summary = RecoveryReportDisplaySummary.From(rows);
        Check(summary.Total == 2 && summary.NeedsReview == 1 && summary.Other == 1 &&
              rows.SequenceEqual(snapshot) && rows.All(x => !x.MayExecute), "Summary modified rows or authorization");
    });
    Test("Version provider reads assembly informational metadata", () => {
        string value = ApplicationVersionProvider.ReadProductVersion(typeof(ApplicationVersionProvider).Assembly);
        Check(!string.IsNullOrWhiteSpace(value) && !value.Contains('+'), "Invalid product version");
    });
    Test("Version provider rejects missing assembly", () => {
        Throws<ArgumentNullException>(() => ApplicationVersionProvider.ReadProductVersion(null!));
    });

}
finally { try { Directory.Delete(root, recursive:true); } catch (Exception ex) { Console.Error.WriteLine("Test cleanup warning: " + ex.Message); } }
Console.WriteLine($"Results: {passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
