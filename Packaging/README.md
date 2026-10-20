# MM Zidoo Sync packaging policy

Current application: **1.1-beta.1**, executable **MMZidooSync.exe**.

PowerShell installer, packaging and installer-test files remain in the local Visual Studio checkout and are deliberately excluded from GitHub. A GitHub clone does not contain those scripts. Existing local installed helpers remain necessary for their uninstall/recovery contract; this cleanup does not delete them.

A source-only build can publish the native application directly:

```text
dotnet publish MediaMonkeyZidooSync/MediaMonkeyZidooSync.csproj -c Release -r win-x64 --self-contained true -o artifacts/windows
```

Publishing builds files; it does not install, sign, upload or release them. A future GitHub distribution must contain no PowerShell files or script-bearing installer archives.

All four legacy uploaded installer ZIPs and matching checksum assets were withdrawn on October20,2026 after preserving verified local copies. Historical release notes remain. No replacement Beta download is published by this cleanup.

Current acceptance, the unresolved sharing failures and local-only tooling boundaries are recorded in ../CURRENT-STATUS.md and ../REPOSITORY-POLICY.md.
