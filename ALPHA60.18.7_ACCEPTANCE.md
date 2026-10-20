# Alpha.60.18.7 cancellation and Run History correction — October 20, 2026

Original user evidence remains unchanged:
E:\Temp\MediaMonkey\RunHistory\20261020-105558-4764811-c1756b9e.log
E:\Temp\MediaMonkey\NativeSync-20261020-105601-613-7112e3\Transaction-214cf0303f914b608d8d17b7d929486a

The blue Update entry marked transaction start, not completion. The transaction has rolled-back.json at10:57:27 and no committed.json. Cancellation interrupted native navigation; the application completed compensation and restored its original off/unreachable state before reporting cancellation at10:57:35.

Original Hootie M3U SHA256:6093AA17C1F116D9557D9B273DB13CCFF461A5A553840335A95CA1C5CA3680A6. Original positions11/12: Only Want To Be With You / Do It Again.
Proposed M3U SHA256:F29254460D6B5B03339D6CF039250E5E0E364A8C59810C2C6CD8FC3F668FB9A3. Proposed positions11/12: Do It Again / Only Want To Be With You.
The TV photo matches the original ordering. This evidence supports expected cancellation/rollback, not a committed update failing to change ordering. No fresh device query or navigation was sent during this investigation.

The presentation defect was real: UserMessages.Cancelled attached the expected exception to Run History, and the transaction wrote cancellation into failure.json. The Run History table also used non-selectable text/full-row selection and a star-sized path column, which prevented full-path horizontal scrolling. Michael confirmed the table was the affected control.

Candidate18.7 removes traces from expected user cancellation, writes a plain cancelled.json receipt, retains failure/recovery diagnostics for genuine errors, labels transaction starts explicitly and emits completion only after commit. A simultaneous cancellation request does not convert an unrelated compensated failure into normal cancellation. Existing transaction rollback and device navigation remain unchanged.

Run History now supports horizontal scrolling and partial text selection. Explicit row-header selection remains available. Double-clicking text selects a word; Open Selected Log remains the action to open a file. Other tables retain their selection behavior. The optional assessment controls/backlog item remain unchanged.

Validation: final11-stage offline/UI suite PASS;30 isolated UI checks against the exact published DLL PASS; exact published cancellation/rollback/timeout/failed-recovery/history checks PASS. Initial broader suite stopped on a disposable-file sharing violation; the final run passed. Cause of that transient sharing violation remains unestablished. Failed evidence remains preserved. The small exact-payload harness also retained its initial missing-import and host-lookup failures; these were verification-harness issues, not application fixes.

Published DLL SHA256:A2BFBCDD4791A5393BD52B5674755A6E342F9A1ADC9549AA35A8E6099592A268. Further package/install/source/CI status is recorded in the final18.7 checkpoint when complete. This patch has not repeated live-device acceptance and does not inherit a new live acceptance result from18.6.
All9 isolated installer file-safety checks passed upgrading the18.6 payload to the packaged18.7 payload. No installer logic changed. Keep repository private and PR1 draft/unmerged; signing and license selection remain deferred.
