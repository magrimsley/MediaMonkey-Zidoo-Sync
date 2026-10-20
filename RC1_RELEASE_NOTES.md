# MM Zidoo Sync — 1.1.0-rc.1

Release candidate for the existing 1.1 release line. This remains a prerelease and is unsigned while the submitted SignPath application awaits a decision.

Changes from Beta 1: canonical three-part version 1.1.0-rc.1; updated product stage; full GPLv3/dependency/runtime/artwork notices included in the portable payload. Local installer metadata accepts existing two-part Alpha/Beta identities and the new three-part RC identity, retaining the installed Beta and settings. PowerShell stays local and is excluded from the GitHub portable release.

No synchronization backend behavior change. MediaMonkey remains the single source of truth. KB-001 remains open and carried forward; successful checks do not establish a fix. Prior Beta acceptance remains historical evidence, not acceptance of this new binary.

MM Zidoo Sync is independently developed and is not affiliated with, endorsed by or sponsored by Ventis Media or Zidoo. Names and marks remain their owners' property; this disclosure grants no trademark rights.

Validation complete: all five native offline suites, all 36 focused UI/configuration checks against the exact published RC DLL, six disposable Beta-to-RC upgrade/uninstall checks and all 38 immutable baseline hash checks passed. Windows CI passed on source dfecf7207b3c468476dad86cde61b3a3a0b25061: https://github.com/magrimsley/MediaMonkey-Zidoo-Sync/actions/runs/39513391666. Installed RC DLL SHA256: 42A872BA065B1C52C1DB9576A110FDD7B045CF0AA4259F0A9689832A7BE03AC5. All 11 existing configuration-file hashes were retained; Beta remains installed. No fresh Sandbox registration/profile test or live synchronization was performed for RC1. User acceptance remains pending. Stable promotion requires the owner's separate decision.

## Distribution

The portable archive contains no PowerShell or installer scripts. Extract and run MMZidooSync.exe; portable extraction does not create shortcuts or replace a registered installation. Existing per-user settings retain precedence. Full GPLv3, runtime/dependency notices and artwork provenance are included. The published portable and installed DLL derive from the locally built candidate tested above; CI separately validated the same source and did not produce the uploaded archive. SignPath approval remains pending.

