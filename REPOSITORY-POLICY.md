# Local-only PowerShell policy

The owner requires PowerShell scripts to remain in the local Visual Studio checkout, outside GitHub source.

- Do not track .ps1, .psm1, .psd1, .ps1xml, .pssc or .psrc files.
- Do not commit package archives that could embed local scripts.
- Keep local build/installer/test scripts and immutable reference hashes intact. Removing a Git index entry must not delete its local file.
- GitHub Actions uses direct .NET commands with the cmd shell. Scripted local installer, Sandbox and interactive desktop checks are separate scopes.
- RepositoryPolicyChecks validates the tracked index and workflow dependencies. Ignore rules alone do not remove already tracked files.
- Local installed copies retain installer helpers required for their existing uninstall/recovery contract. This GitHub cleanup does not delete working installer helpers or historical evidence from disk.
- Future GitHub release assets must contain no PowerShell. The existing local PowerShell-assisted installer packages are not eligible for re-upload.
- Normal commits remove files from the branch's current tree. They do not erase historical commits, tags or automatic source archives. Rewriting published history is a separate owner decision.

Build from GitHub source with dotnet; local-only script instructions do not apply to a fresh GitHub clone.

The ignored local solution MediaMonkeyZidooSync/MediaMonkeyZidooSync.Local.sln retains the script solution items for Visual Studio. The shared solution omits missing local-only script items.
