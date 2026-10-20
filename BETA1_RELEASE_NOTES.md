# MM Zidoo Sync - Beta 1

Approved product identity:
- Application name: MM Zidoo Sync
- Executable: MMZidooSync.exe
- Version: 1.1-beta.1
- About and Help description: MediaMonkey → Zidoo playlist synchronization

The executable assembly and pack-resource identifiers follow the new name. Existing artwork, two-second splash behavior, tray icon and synchronization workflow remain. Settings/history directories, installer root and internal ownership identifiers retain legacy names for continuity. The installer accepts retained Alpha versions and the new Beta version, creates the renamed desktop/Start shortcuts, and removes only verified owned legacy shortcuts after creating the replacements. Prior installed payloads remain available.

Beta is a release-stage label. The unresolved backend sharing issue remains documented; this rename does not resolve it or establish a green full backend regression. No live Zidoo commands or remote GitHub publication form part of this update.

## Verified local delivery

Installed 1.1-beta.1. All36 focused checks passed against the exact published DLL; all9 isolated Alpha18.9-to-Beta1 upgrade/uninstall checks passed. Installed startup passed with splash disappearance observed2.0063seconds after main UI. Installed DLL SHA256:53A34533EAB6CD94B8CF0926E12D4A6D5F6BF3BF13348DD1E28F6C971444648E. Installed payload matches the packaged binary. Existing settings/archive hashes unchanged; prior Alpha payload retained; new desktop/Start shortcuts and Beta display name verified; owned legacy shortcuts absent. All38 immutable baseline hashes match. No full backend regression or live synchronization claim; existing sharing issue remains open. No GitHub push, CI or prerelease publication for this Beta build.
## Subsequent user and isolated Windows acceptance

Michael confirmed the installed MM Zidoo Sync name, 1.1-beta.1 label, retained configuration and approved About/Help description all pass. Seven isolated Windows Sandbox registration/uninstall checks passed against the exact installed DLL hash. The separate eleven profile/migration checks also passed against the exact binary. Both isolated guest receipts were verified before shutdown. KB-001 remains a carried Known Issue and is excluded from the outstanding-work checklist at the owner's direction; it is not marked fixed.