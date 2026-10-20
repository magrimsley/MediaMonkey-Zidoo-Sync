# Native architecture and recovery — Alpha.60.18.6

WPF is the presentation layer. SynchronizationWorkflow gates validation, exporter verification, plan review and execution. SyncRunner composes the native service; the exporter reads SQLite in read-only mode. NativeProductionSyncService compares reviewed content hashes, owns the NAS run lock and device/power lifecycle, and delegates each playlist operation to NativeProductionTransaction. NativeZidooPlaylistDevice owns model verification, inventory parsing and guarded screen navigation. No synchronization operation launches PowerShell or another backend process. SyncRunner executes native synchronization away from the WPF synchronization context. Progress is queued to the dispatcher asynchronously so device work does not wait for display updates. This is a responsiveness safeguard; it does not establish the cause of the reported laptop freeze.

## Transaction scope

Each playlist is a transaction, matching the preserved production PowerShell loop. Earlier committed playlists remain committed if a later playlist fails. A verified compensated failure can permit remaining transactions; cancellation or unverified restoration stops the batch. This is per-playlist compensation, not a distributed database ACID commit or whole-run rollback.

Before mutation, the transaction flushes original and candidate M3Us, intent, original inventory, collateral backups and a schema-2 pending marker containing evidence hashes. File publication uses same-directory staging/replacement. Commands require the expected model and inventory identity. Final file hash, target count and unrelated identities/counts are checked before commit. Caught failures use an independent recovery token. Conflicting state retains the marker and evidence.

## Recovery after termination

NativeProductionRunState records device settings, playlist root, report location and initial control-port reachability before wake or playlist operations. A later Run first reconciles the retained run and pending transaction under the production lock. It does not execute a newly prepared plan after recovery.

NativeInterruptedRecovery checks storage boundaries, reparse points, evidence hashes, original/candidate content and inventory. It keeps a verified final state, or restores the original M3U and imports a missing original playlist. A verified final state requires no destructive replay. Crash recovery does not delete an uncertain target. Legacy markers without the new evidence binding, external edits, changed settings and unrecognized states stop safely and retain evidence.

The coordinator restores recorded off/unreachable ownership, including interruption before a playlist transaction. For an initially reachable device, it restores API readiness. Recovery completion is durable and reported separately from synchronization success. The UI resets to validation and review. There is no separate normal recovery button.

Real process-kill tests terminate workers at preparation, file publication, device deletion, device import and commit; recovery is terminated during file restoration, device restoration and receipt publication. A persistent fake device models process-independent state. These tests cover application termination/restart; they do not prove physical NAS power-loss durability, every firmware behavior or immunity to concurrent external writers.

## Isolation and diagnostics

The NAS lock serializes cooperating app runs. Hash and inventory checks detect observable external edits; other software and remote controls do not participate. Device verification establishes playlist identity and track count, not playback of every song or every remote track URI/order. Reachability is the observable power-state criterion; TV/AVR behavior is outside this contract.

Full exceptions are saved in diagnostic logs, never rendered in synchronization output or copied visible text. Migration, installation, offline regression, exact-package Sandbox tests and explicitly authorized live acceptance remain separate scopes. Legacy explicit recovery is retained for old reviewed receipts; new automatic recovery does not weaken its safeguards.
## Current candidate acceptance blockers

Alpha.60.18.1 passed a clean 11-stage offline regression and isolated create/update/cancel/process-termination recovery checks. Its live wake check failed; a later request restored readiness. Alpha.60.18.3 stages bounded repeated wake requests every 15 seconds within the existing readiness timeout, tested with a simulated clock. Live acceptance of that change is pending.

A subsequent disposable-playlist cleanup targeted an existing playlist incorrectly. Compensation restored its 32-track count, with a new device ID. Inventory verification after a destructive command cannot establish which tile was selected before that command. Further live mutations and release promotion are paused until navigation targeting has independent verification. Neither this candidate nor the prior isolated success establishes production acceptance.

## Alpha.60.18.3 parity correction

Restores production wrapper Refresh-PersistentPlaylistGridAfterMutation immediately after verified intended deletion, before any following import/targeting. Uses its exact Up20(250ms),Down2(400ms),Ok(1000ms) sequence, with model checks and fresh inventory. Native inventory comparison additionally refuses unexpected changes during refresh. No unconditional Right was added. Frozen PowerShell is unchanged. This restores a documented omission, not a claim that targeting is now proven correct. New simulated transport scenarios cover refused refresh and changed fresh inventory. Live navigation/acceptance remain pending.

## Alpha.60.18.4 visible-order prerequisite correction

October 19 video showed a newest-first rendered grid while sort=0 API inventory used name order. Position74 opened Hootie & Smash Mouth instead of the disposable playlist. The immutable inventory component explicitly requires validating API order against the visible grid; its Focus function does not establish that prerequisite. A checked A-Z preference also coexisted with the stale newest-first grid.

Before target row/column movement, this candidate uses the frozen anchor and first-tile entry, moves Left to the visually validated Sort control, opens Sort, clamps to its top, selects Z-A then A-Z to force a rebuild, and closes with the user-verified Back key. It then reuses the frozen anchor/first-tile entry before the original API-derived movement and pre-activation inventory guard. No inventory request occurs between the two sort selections. Import and deletion-confirmation mechanics and the Alpha.60.18.3 post-delete refresh remain unchanged.

This is an explicitly documented prerequisite extension, not a sort sequence found in the immutable scripts. Frozen PowerShell remains unchanged. Michael visually validated the diagnostic sequence on his device, including the resulting A-Z first row and closed menu. API acknowledgements cannot independently prove rendered UI state. Full candidate cold/warm guarded checks and live acceptance remain required; do not describe a successful simulated test or preference check as verified visual targeting.

Thirty new stateful fake-device scenarios cover newest/name/reverse/stale-preference ordering, every normalization-key rejection, and cancellation at every normalization key. Those scenarios verify refusal before deletion, not real firmware behavior. Hootie & Smash Mouth was manually removed by Michael afterward and remains an explicitly deferred twelve-track restoration obligation; original IDs and full playback equivalence are not guaranteed.

Alpha.60.18.5: after the documented sort prerequisite, Back then Right returns directly to the first grid tile, as visually confirmed October 19. Removes the added second app launch/home anchor. This prerequisite remains an explicit extension; frozen scripts remain unchanged. Full live acceptance remains required.

Alpha.60.18.6: post-delete refresh reuses the frozen Focus app-open/3s/identity/home anchor before wrapper inventory verification. Observed Up20/Down2 alone remains inside grid or tracks. This composes existing script mechanics to establish required starting context; immutable wrapper refresh itself does not include app-open. Targeting sort/direct return unchanged. Fresh acceptance required.
