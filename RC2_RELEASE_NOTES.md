# MM Zidoo Sync — 1.1.0-rc.2

Requested changes: preserve per-user layout across application versions; restore Open Selected Log when selecting text within a Run History cell; disable Review Changes and Run when verification finds no pending work; add a script-free MSI installer as well as portable distribution.

Layout repair: isolated UI tests now use their isolated profile for layout, preventing them from overwriting the real user's layout. Restore the saved tab, window geometry, filters/expansion settings and grid-column widths/order from the stable version-independent user profile. Earlier lost preferences cannot be reconstructed from a file already overwritten by prior tests.

The MSI is per-user, with no PowerShell/custom actions or paid installer-tool dependency. It shares the existing user-state directory, preserves unowned data, and uses separate ownership from the legacy local installer. See Packaging/MSI-README.md for transition behavior and the internal numeric MSI version sequence.

RC1 version display acceptance passed: Michael confirmed 1.1.0-rc.1 in About and the lower-right label. These RC2 changes require new acceptance. SignPath application remains submitted/pending; RC2 will remain unsigned. KB-001 is carried forward, not marked fixed. MediaMonkey remains the source of truth. No live sync is authorized by installer testing.

Validation: exact application DLL passed 43/43 isolated UI checks and all five offline suites in the preceding checkpoint. On October 21, the corrected MSI passed full local fresh installation, actual RC1-payload upgrade, comparison of all 423 payload hashes, deleted-license repair, downgrade refusal, shortcut creation/targets/removal, and uninstall retention of an unowned file. All 13 existing user-profile files and the registered RC1 DLL retained their hashes. All 39 SourceReference entries match SOURCE-SHA256.json. No fresh Sandbox or live-device acceptance is claimed.

The initial full MSI failed before installation with error 2725 (invalid database tables). The builder incorrectly used a free-form msquic.dll display version beyond MSI limits. It now uses the fixed numeric file version and valid short directory aliases. Failed artifacts/logs remain retained locally.

Delivery: prepared only. PR #2 remains draft; main, public RC1 and the registered legacy RC1 application remain unchanged. Owner approval is required before merge, unsigned public RC2 publication, and registered application update. Stable promotion remains a separate owner decision.

Transaction architecture clarification: no playlist differences with no recovery records disables Review/Run. Retained transaction/run markers count as pending work and retain the existing validation/review/locking/reconciliation path; no transaction safeguards or backend recovery behavior are bypassed.
