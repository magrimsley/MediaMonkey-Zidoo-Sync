# MM Zidoo Sync 1.1.0-rc.9

Application 1.1.0-rc.9; file 1.1.0.9; MSI 1.1.9. Owner acceptance remains pending.

## Synchronization corrections

Publication and rollback use temporary names without a leading dot. Share-backed tests reproduced the RC8 defect: a dot-prefixed temporary file became hidden and File.Replace carried that visibility into an existing M3U. RC9 keeps durable writes and atomic replacement, records original Hidden/System flags in transaction intent, verifies visibility before commitment and after rollback, and retains recovery gates when it cannot verify visibility. It does not silently unhide existing files.

Frozen visible-file planning and exact-byte SHA256 rules remain unchanged. No frozen PowerShell script changes. A CREATE with an existing physical target and existing populated device playlist now stops before playlist transactions rather than publishing bytes and implying that equal counts prove correct device contents.

## Explicit repair

After validation and exporter verification, Review Changes remains available even when M3U files match. Choose Repair Existing Playlists, select only the populated managed playlists needing reimport, and add those repairs. The reviewed plan labels repairs as delete-and-reimport. No playlist is selected by default. Run Synchronization executes only after plan approval, checks reviewed bytes/visibility and current device state, and refuses a selected repair when the required file or existing device playlist is absent.

This repair is an explicit application recovery choice; it does not reinterpret the frozen default CREATE branch as UPDATE. Backups, guarded deletion, journals, cancellation, collateral-state checks and interrupted-run recovery remain active. Repair recovery cannot treat an unchanged hash/count and the original device identity as proof that reimport occurred.

When stale Zidoo contents differ from an already matching M3U, the application cannot reconstruct that unknown former song order from count-only device inventory. Recovery uses retained M3U content. Completion text states that file contents and device track counts passed verification and asks the owner to confirm song order on Zidoo.

## Validation

Isolated transaction tests cover same-count reordering, explicit repair with an already matching file, hidden-target refusal, cancellation/rollback visibility, metadata drift, and retained recovery gates. UI tests exercise repair selection and authorization plus stale-file/metadata refusal. Native process-kill tests include repairs interrupted before and after reimport. Share tests use a disposable folder and a simulated device. Live device acceptance remains separate.

Normal upgrades retain configuration and preferences. Startup timing and general selection of playlists for normal synchronization remain separate backlog items.
