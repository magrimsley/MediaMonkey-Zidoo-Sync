## Unreleased RC2 acceptance follow-ups

- Embed the application icon for Windows Programs and Features.
- Highlight the Run History cell selected by mouse or keyboard while retaining selectable text and the exact log target; keep the highlight when opening its log.
- Owner confirmed the MSI entry appeared and uninstall removed it; MediaMonkey change detection worked. No updates were sent to Zidoo in this check.

# MM Zidoo Sync — current

Prepared candidate: 1.1.0-rc.2; registered installation and public release remain RC1. See RC2_RELEASE_NOTES.md and CURRENT-STATUS.md. Historical entries retain their original scope.

## 1.1.0-rc.2 (prepared; release approval pending)

- Preserve version-independent layout and selected Run History log behavior; keep clean no-change Review/Run disabled while preserving reviewed recovery.
- Add a native, script-free per-user MSI alongside the portable package.
- Author MSI file versions from numeric fixed-file metadata. Free-form display versions such as msquic.dll 2.5.10.154561281 violate MSI limits; its numeric version is 2.5.10.0.
- Full local MSI checks passed: fresh RC2, RC1-payload upgrade, all 423 payload hashes, repair, downgrade refusal, shortcuts, uninstall and data retention. User configuration and registered legacy RC1 remained unchanged.

# MediaMonkey Zidoo Sync — 1.1-alpha.54

- Based on alpha.53a report browser source and the alpha.53b journal contention correction.
- Added read-only recovery report status counts to existing Run History / Logs UI.
- Added four isolated tests for status counting and non-mutating behavior (90 expected total).
- Preserved SourceReference .bat/.ps1 bytes; no production sync, recovery execution, or device calls added.
- Windows compilation and WPF visual inspection remain to be completed by the user.
