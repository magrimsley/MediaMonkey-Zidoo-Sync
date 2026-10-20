# GitHub history cleanup — approved and applied

Owner approved the five exact reference updates after reviewing the rehearsal on 2026-10-20. The atomic, leased update completed; historical procedure text below records the plan used. No repository visibility change was authorized or performed.

## Scope

The current branch already excludes PowerShell, with all local script hashes preserved. The historical inventory also identifies fifteen script paths across locally available refs. Final preparation must fetch/inventory every remote branch and tag, including filename changes and any script-bearing Git objects outside that initial inventory.

Uploaded script-containing release ZIPs and matching checksum files have already been removed, with verified copies retained in the local Visual Studio checkout. Branch deletion commits alone do not remove files from old commits or tag source archives.

## Proposed procedure

1. Agree on a maintenance window and pause other pushes. Inventory remote branches, tags, PR1 and release/tag relationships. Preserve the local working tree separately.
2. Save a verified full Git backup inside ignored local-only storage. Keep immutable PowerShell originals and their hashes untouched. Export PR/release metadata and all old-to-new reference information locally.
3. Create a separate disposable mirror; never filter the active Visual Studio checkout. Select and test a maintained history-filtering tool. Remove all inventoried PowerShell paths from every intended branch/tag history; inspect for script-bearing archives and copied/renamed variants.
4. Produce a report of rewritten commit IDs, affected tags, branches and PR references. Scan every reachable rewritten tree, verify no forbidden paths remain, build the native application and run the offline policy/tests. Check that local installed application/settings remain untouched.
5. Present the exact reference changes, checks and rollback procedure for explicit approval. Do not force-push before that approval.
6. If approved, update the intended remote branch/tag refs in the agreed window. Reconcile release tags and PR1; obtain approval before closing/recreating the PR or changing protected-branch settings.
7. Verify through a fresh clone and source archives for rewritten tags. Re-establish repository policy checks, review any CI artifacts/caches separately, and confirm no uploaded script package remains.
8. Coordinate all other clones/forks so old history is not merged or pushed back. Retain the old Git backup locally only.

## Limits and decision points

Rewriting history changes commit IDs and can disrupt pull-request diffs and existing clones. Old commits can remain reachable through pull-request refs, forks or server caches. Do not promise complete server-side erasure. GitHub states that Support does not remove non-sensitive data through its sensitive-data cleanup process; these scripts have not been identified as leaked secrets.

Reference: [GitHub documentation on removing data from repository history](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository).

If eliminating every historical GitHub access path is mandatory, assess a new clean repository and controlled retirement of the old repository as a separate proposal. Do not delete the existing repository, release tags or PR history without explicit approval.

## Current disposition

Applied after a verified backup and local rehearsal: two branches and three tags were rewritten atomically with exact-old-value leases. All 23 commits were retained; 15 PowerShell paths were removed and every remaining file blob was preserved. The rewritten copy built successfully and passed the policy check and all five native offline suites. Release descriptions record old/new commit IDs; application assets/checksums are unchanged. Old PR refs and caches may retain prior history, so complete erasure is not claimed. The repository remains private. Local script hashes remained unchanged.

