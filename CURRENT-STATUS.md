# MM Zidoo Sync — current status

Current installed release candidate: **1.1.0-rc.1**. Previous Beta 1 is retained. Application: **MM Zidoo Sync**. Executable: **MMZidooSync.exe**. About and Help: **MediaMonkey → Zidoo playlist synchronization**.

This page supersedes pending-state wording in historical Alpha notes. Those notes remain historical evidence.

KB-001 remains documented in KNOWN-BUGS.md as a carried known issue, not an item in this outstanding-work checklist.

## Outstanding work outside the feature backlog

1. **Beta promotion complete.** The owner approved merging and promotion on 2026-10-20. PR #1 merged into main at 961af80ae192a93c29e986f48659ae3b0a4c9c72. The owner approved public repository and Beta distribution on 2026-10-20. Version 1.1-beta.1 remains a prerelease; stable promotion remains a separate decision.
2. **Deferred release decisions.** Project-owned source now uses GPL-3.0-only, with voluntary donations. The owner submitted the SignPath Foundation application on 2026-10-20; approval and signing integration remain pending. The release candidate remains unsigned. The Beta 1 dependency notice bundle and owner artwork grant are documented. The owner approved retaining the Gemini artwork with an independent-project disclaimer. That disclosure grants no trademark rights and does not assert brand-owner clearance; see ARTWORK-PROVENANCE.md. Security-reporting contact: [magrimsley@hotmail.com](mailto:magrimsley@hotmail.com), approved by the owner on 2026-10-20.
3. **Historical Git cleanup.** The owner-approved rehearsal and five-reference rewrite completed on 2026-10-20; maintained branches and tags exclude historical PowerShell. Release notes map old to new source IDs. Old PR refs/caches may retain prior history; complete erasure is not claimed. See GITHUB-HISTORY-CLEANUP-PLAN.md.

Current delivery: [public Beta prerelease](https://github.com/magrimsley/MediaMonkey-Zidoo-Sync/releases/tag/v1.1-beta.1), [passed native Windows CI](https://github.com/magrimsley/MediaMonkey-Zidoo-Sync/actions/runs/39504792477). Release source commit:103af91006b7142d2fdbe7b199070bdbb04cf60a. The portable ZIP contains zero PowerShell files. The default branch also contains no PowerShell; GitHub language metadata reports C# only.

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

