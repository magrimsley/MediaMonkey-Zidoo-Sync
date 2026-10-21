# MM Zidoo Sync — current status

Latest public release candidate: **1.1.0-rc.2**, unsigned. PR #2 merged and RC2 publication/local update completed October 21, 2026. The owner subsequently tested a first-time MSI installation and uninstalled it. The MSI appeared in Programs and Features, created shortcuts, launched with its splash screen, displayed About/Help, discovered a powered-on Zidoo, and removed its entry/shortcuts on uninstall. No playlist changes were sent to Zidoo in that acceptance run.

Unreleased follow-ups are prepared in draft PR #3: installer icon and optional desktop shortcut, selected-log highlight, Configuration-first synchronization gating, discovery history and subnet-derived WOL broadcast, path pickers, and corrected Help. These changes are not part of published RC2. Full interactive installer/picker visual acceptance remains pending. Source checks and isolated results belong to the follow-up PR, not the earlier RC2 binary.

Current delivery: https://github.com/magrimsley/MediaMonkey-Zidoo-Sync/releases/tag/v1.1.0-rc.2

Outstanding: final follow-up package identity/acceptance and owner release decision; SignPath response/signing integration; stable promotion decision; backlog items including retention and unsaved-edit confirmation. Legacy Alpha registration was reported by the owner, but a current owner-context check of all uninstall views and native MSI products found none; do not claim a surviving record was removed without evidence.

MediaMonkey remains the source of truth. KB-001 remains documented separately and is not an ordinary outstanding checklist item. No Sandbox or live synchronization was resumed. PowerShell tooling and immutable references remain local-only.

## Historical status below

The following entries describe earlier release checkpoints and do not supersede the current RC2/follow-up status above.

## Outstanding work outside the feature backlog

1. **Beta promotion complete.** The owner approved merging and promotion on 2026-10-20. PR #1 merged into main at 961af80ae192a93c29e986f48659ae3b0a4c9c72. The owner approved public repository and Beta distribution on 2026-10-20. Version 1.1-beta.1 remains a prerelease; stable promotion remains a separate decision.
2. **Deferred release decisions.** Project-owned source now uses GPL-3.0-only, with voluntary donations. The owner submitted the SignPath Foundation application on 2026-10-20; approval and signing integration remain pending. The release candidate remains unsigned. The Beta 1 dependency notice bundle and owner artwork grant are documented. The owner approved retaining the Gemini artwork with an independent-project disclaimer. That disclosure grants no trademark rights and does not assert brand-owner clearance; see ARTWORK-PROVENANCE.md. Security-reporting contact: [magrimsley@hotmail.com](mailto:magrimsley@hotmail.com), approved by the owner on 2026-10-20.
3. **Historical Git cleanup.** The owner-approved rehearsal and five-reference rewrite completed on 2026-10-20; maintained branches and tags exclude historical PowerShell. Release notes map old to new source IDs. Old PR refs/caches may retain prior history; complete erasure is not claimed. See GITHUB-HISTORY-CLEANUP-PLAN.md.

Current delivery: [public RC1 prerelease](https://github.com/magrimsley/MediaMonkey-Zidoo-Sync/releases/tag/v1.1.0-rc.1), [passed RC1 Windows CI](https://github.com/magrimsley/MediaMonkey-Zidoo-Sync/actions/runs/39513391666). RC1 binary source commit: dfecf7207b3c468476dad86cde61b3a3a0b25061. The portable ZIP contains zero PowerShell files. The default branch also contains no PowerShell; GitHub language metadata reports C# only.

## Verified and closed

- Beta user acceptance: all four identity/configuration/About/Help checks passed.
- Exact Beta binary: seven fresh-Sandbox registration/uninstall checks and eleven profile/migration checks passed. Networking/vGPU disabled, one 2 GB guest at a time; receipts verified before shutdown.

- Local Beta executable, names, description, artwork, About/Help and two-second splash.
- Desktop and Start menu migration; settings/archive hashes unchanged; previous Alpha retained.
- Run History horizontal scrolling and partial text selection.
- Plain intentional cancellation logging; transaction starts distinguished from verified completion.
- The user accepted successful synchronization and cancellation on Alpha18.7.
- The user confirmed restored playlists exist and selected songs play.
- Alpha18.6 fresh-profile/registration checks completed; they are not still pending.
- Optional diagnostics controls removed and handlers commented out.

MediaMonkey is the single source of truth. Auditing every Zidoo entry/order, importing Zidoo changes into MediaMonkey, and proving full remote playback equivalence are outside this version's scope.

## Repository policy

PowerShell files and package archives are local-only and excluded from Git tracking. The CI policy check rejects them and PowerShell-dependent workflows. Local files, including immutable references, remain preserved. See [repository policy](REPOSITORY-POLICY.md), [known bugs](KNOWN-BUGS.md), and [feature backlog](BACKLOG-v1.0.0.txt).





## Release candidate preparation

The owner requested RC promotion. Version 1.1.0-rc.1 follows the existing 1.1 Beta line. RC automated validation and local installation verification passed; user acceptance remains pending. Windows CI39513391666 passed. Earlier Beta checks are historical. See RC1_RELEASE_NOTES.md.

