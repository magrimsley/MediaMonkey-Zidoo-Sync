# MM Zidoo Sync — 1.1.0-rc.2

Requested changes: preserve per-user layout across application versions; restore Open Selected Log when selecting text within a Run History cell; disable Review Changes and Run when verification finds no pending work; add a script-free MSI installer as well as portable distribution.

Layout repair: isolated UI tests now use their isolated profile for layout, preventing them from overwriting the real user's layout. Restore the saved tab, window geometry, filters/expansion settings and grid-column widths/order from the stable version-independent user profile. Earlier lost preferences cannot be reconstructed from a file already overwritten by prior tests.

The MSI is per-user, with no PowerShell/custom actions or paid installer-tool dependency. It shares the existing user-state directory, preserves unowned data, and uses separate ownership from the legacy local installer. See Packaging/MSI-README.md for transition behavior and the internal numeric MSI version sequence.

RC1 version display acceptance passed: Michael confirmed 1.1.0-rc.1 in About and the lower-right label. These RC2 changes require new acceptance. SignPath application remains submitted/pending; RC2 will remain unsigned. KB-001 is carried forward, not marked fixed. MediaMonkey remains the source of truth. No live sync is authorized by installer testing.

Validation and delivery: pending. Stable promotion remains a separate owner decision.

Transaction architecture clarification: no playlist differences with no recovery records disables Review/Run. Retained transaction/run markers count as pending work and retain the existing validation/review/locking/reconciliation path; no transaction safeguards or backend recovery behavior are bypassed.
