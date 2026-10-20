# MM Zidoo Sync — current status

Current local version: **1.1-beta.1**. Application: **MM Zidoo Sync**. Executable: **MMZidooSync.exe**. About and Help: **MediaMonkey → Zidoo playlist synchronization**.

This page supersedes pending-state wording in historical Alpha notes. Those notes remain historical evidence.

KB-001 remains documented in KNOWN-BUGS.md as a carried known issue, not an item in this outstanding-work checklist.

## Outstanding work outside the feature backlog

1. **GitHub review and delivery.** The default branch contains no PowerShell, and GitHub language metadata reports C# only. Beta application changes are reviewed separately in PR1, which remains draft/unmerged. CI must pass for the release commit before private unsigned prerelease publication. See the PR and release for delivery status.
2. **Script-free distribution.** Legacy script-bearing ZIPs/checksums were withdrawn with local backups. The Beta download uses portable native application files, without a PowerShell installer. Local installer tooling remains in the Visual Studio checkout.
3. **Deferred release decisions.** Signing and project licensing remain undecided; the private Beta remains unsigned. Select a security-reporting contact before public distribution. These are decisions, not additional synchronization features.

A separate GitHub history-cleanup plan has been prepared in GITHUB-HISTORY-CLEANUP-PLAN.md. It has not been executed; old commits/tags remain unchanged.

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
