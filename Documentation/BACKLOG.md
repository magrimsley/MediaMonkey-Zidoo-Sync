# Product Backlog


## Deferred and Follow-up Work

- **BACKLOG — Explicit repair of existing Zidoo playlists.** The RC9 forced delete/reimport UI, planner, executor options, and tests are commented out and excluded from the compiled application at the owner's request. This is outside the frozen PowerShell scope. Any future implementation needs separate scope approval, an unambiguous workflow and Help, ordered-content verification, recovery design, and owner acceptance. Do not confuse this proposal with the frozen REPAIR_CREATE behavior for missing playlists during a changed run.
- **BACKLOG — Choose playlists for ordinary synchronization.** Allow selection beneath the configured MediaMonkey parent. Define deselection separately from deletion; retain review, transaction and recovery safeguards.
- **BACKLOG — Unsaved configuration changes.** Offer Save / Discard / Cancel before leaving Configuration or closing; preserve validation and archive behavior.
- **OPEN — Startup responsiveness.** Measure launch-to-visible and launch-to-ready timing. The current splash remains visible for two seconds after first render.
- **IMPLEMENTED; OWNER ACCEPTANCE PENDING — Shared-storage file visibility.** Non-dot temporary names avoid the reproduced hidden-file replacement defect. Publication, rollback and recovery verify retained visibility without automatically unhiding existing files.

## Commercialization / Windows Integration

- Design a trial version and define which capabilities remain available during the trial.
- Design a license-key/activation mechanism that unlocks full application functionality.
- Complete licensing design before installer hardening because licensing may affect installation, upgrades, machine/user identity, ProgramData, and Registry use.
- Evaluate the Windows Registry where it provides a Windows-native benefit, including installation metadata, licensing/registration state, installer integration, or appropriate machine-level settings.
- Keep configuration, logs, configuration archives, and Last Known Good in their appropriate application-data locations unless a specific technical reason supports Registry storage.

## Playlist Targeting Workflow Validation

- Deliberately test playlist targeting with internal grid-column assumptions of 4, 5, and 7.
- Verify that an incorrect grid-column assumption cannot cause UPDATE or DELETE to target the wrong Music Player 8 playlist.
- Investigate whether Music Player 8 provides a list view and whether display resolution changes the visible playlist grid layout.
- Keep Playlist Grid Columns hidden from end users until the targeting workflow is proven safe.


## Historical Native Synchronization Migration Checklist

The following entries record the original migration plan; they are not a current release-status checklist. Refer to release notes and validation evidence for completed work.

- **ACTIVE — Native MediaMonkey playlist reading and export.** Alpha.19 introduces the native C# database reader/exporter and a validation-only UI path. Prove output equivalence before connecting it to full synchronization.
- **BACKLOG — Native change detection and reconciliation.** Preserve CREATE / UPDATE / DELETE / MATCH semantics and the no-change/no-Zidoo-contact rule.
- **BACKLOG — Native Zidoo inventory and power management.** Preserve power-state detection, Wake-on-LAN, readiness, cold-boot stabilization, and original-power-state restoration.
- **BACKLOG — Native playlist transactions.** Migrate CREATE, proven UPDATE, DELETE, verification, recovery, and stale-grid refresh behavior.
- **BACKLOG — Playlist-targeting workflow validation before UPDATE/DELETE enablement.** Test incorrect internal grid assumptions (4/5/7), possible Music Player 8 list view, and display-resolution effects; an incorrect assumption must never target the wrong playlist.
