# Exact-candidate live acceptance

This **explicit opt-in** test contacts the configured Zidoo and creates, updates, cancels, recovers and deletes its own disposable device playlist. It also exercises wake/off restoration. It is excluded from automatic CI and must not be run against an unreviewed device or while someone uses its remote.

The test opens the user's MediaMonkey database read-only and makes a SQLite backup beneath ResultsRoot. Only that copy is edited. MediaMonkey's IUNICODE collation is registered in the test copy using ordinal case-insensitive comparison for the synthetic ASCII test name; this does not change the application's read-only exporter or the source database. Two existing readable music tracks are selected; no music files are copied, modified or deleted.

A uniquely owned `00-MMZS-Test-<nonce>` NAS directory is created immediately beneath the configured music root. It contains copies of authoritative M3Us so existing managed playlists remain unchanged and verified collateral backups are available. The test refuses an existing destination, a used test name or a baseline requiring unrelated device repair. Every reviewed change must target `MMZS Acceptance <nonce>.m3u`. It compares unrelated device identities/counts and original M3U hashes throughout the test.

The preferred `--native` mode uses the exact candidate native runner without creating a WPF window, dispatcher pump or screen capture. Offline WPF regression checks button gating and message presentation separately. The legacy window mode exercises MainWindow and routed events, but its interrupted run is not accepted as completed UI coverage. The application DLL loaded by the host must match the explicitly supplied SHA256. The user's normal configuration is not replaced. Detailed results and the copied database remain local and must never be committed or uploaded.

The child interruption worker is terminated only after the native device adapter verifies deletion of the owned test playlist. Its durable transaction and run marker are then recovered through the ordinary native service recovery path. Current changes require a new review afterward. Cancellation, green completion, stack-trace separation, button gating, no-change behavior and off-state restoration are also checked.

After a failure, keep all evidence and inspect the transaction/device state before retrying. The test does not automatically erase uncertain state. After success, only the verified owned NAS mirror is removed; the device test playlist is deleted through the native engine, and the original observed power state is restored. Physical playback and TV/AVR state require separate evidence.

Build with an explicit published payload directory, then run only after authorization:

```powershell
dotnet build .\Testing\InstalledWorkflowAcceptance\InstalledWorkflowAcceptance.csproj -c Release -p:CandidateDirectory=C:\Temp\ReviewedPayload
dotnet .\Testing\InstalledWorkflowAcceptance\bin\Release\net9.0-windows\InstalledWorkflowAcceptance.dll --native C:\Path\To\Settings.json C:\Temp\AcceptanceResults EXPECTED_APPLICATION_DLL_SHA256
```

The current test targets application version 1.1-alpha.60.18.6. Its worker option is internal test infrastructure, not an application synchronization command or a production PowerShell backend.

Run this test serially: no Sandbox or desktop capture alongside it. The original NAS synchronization lock is held throughout acceptance to prevent a normal application run overlapping the disposable-device test. Power ownership and progress receipts are flushed durably. A previous laptop freeze has no established cause; headless execution does not establish that cause or prove it resolved.

If a failed wake check leaves its disposable playlist, `--cleanup-owned FIXTURE_JSON` is a separate reviewed cleanup mode. It requires the exact candidate DLL, ownership sentinel, unchanged unrelated inventory, no recovery markers and an already reachable device. It deletes only that fixture's playlist and known NAS mirror. It does not retry wake or convert failed acceptance into PASS. Retain the original failure receipt.
