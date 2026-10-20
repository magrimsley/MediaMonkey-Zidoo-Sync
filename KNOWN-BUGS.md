# Known bugs

## KB-001: Intermittent temporary transaction-file sharing failure

**Observed in:** Alpha60.18.7 and Alpha60.18.8; carried forward as unresolved in 1.1-beta.1  
**Status (October 20, 2026):** Open; intermittent sharing failures; cause unidentified. The earlier diagnostic capture did not reproduce the failure; a later 18.8 offline regression encountered another sharing failure as detailed below.

An offline transaction test occasionally fails with a file-sharing exception while reading the newly written `collateral-backups.json` transaction evidence file. The failure occurs before that transaction writes its pending marker or publishes its new target playlist file. This observation does not imply that earlier transactions in a run made no changes.

The failure occurred in the original regression run and recurred in a later unmodified test replay. It is therefore intermittent, rather than never reproduced. The latest investigation did not reproduce it across 200 targeted repetitions, 10 full test repetitions, and nine complete full repetitions under a Windows file-access trace. A tenth traced repetition reached the planned time limit and remains incomplete.

The conflicting process or operation remains unidentified. The evidence does not establish a timing root cause or implicate antivirus software. Passing subsequent tests does not establish that the defect is fixed. We have not confirmed this failure during an installed application synchronization; the observed failures occurred in offline tests using disposable files and simulated devices.

No verified workaround exists. Retain the failed run's log and transaction evidence. Diagnosis requires capturing the conflicting file access when the failure recurs. This issue is separate from the earlier resolved test-fixture `.mmzs-production.lock` acquisition issue.

### Additional observation in Alpha60.18.8

The October 20 offline regression also encountered a sharing exception in `NativeInterruptedRecovery.Save` while moving the pending-marker temporary file into place (`NativeProductionTransaction.ApplyAsync`, line 101; NAS-only empty-playlist deletion test). This is a different failing operation from the earlier `collateral-backups.json` read. A common cause remains unproven. The broader regression run failed and its evidence was retained; UI/artwork checks passed separately. No synchronization backend code changed for 18.8.

Beta1 delivery note: native Windows CI passed on the released source commit. KB-001 remains a carried known issue; no application fix was made or claimed. The owner requested excluding it from the separate outstanding-work checklist.
