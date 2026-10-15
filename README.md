# MediaMonkey Zidoo Sync

Windows WPF alpha for preparing and inspecting synchronization plans between MediaMonkey playlists, playlist storage and a Zidoo player. The application version is 1.1-alpha.60.16.1. **Production synchronization is disabled.** This alpha does not provide an enabled end-to-end production sync workflow.

This is a local public-source preparation candidate, not a published repository or release. The project license is undecided; no open-source license grant has been selected. See [LICENSE-DECISION.md](LICENSE-DECISION.md).

## Build and test

Use Windows with the .NET 9 SDK and Windows PowerShell 5.1. Open MediaMonkeyZidooSync/MediaMonkeyZidooSync.sln in Visual Studio, or build from the repository root:

```powershell
dotnet build .\MediaMonkeyZidooSync\MediaMonkeyZidooSync.csproj -c Debug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Testing\Build-CI.ps1 -NoPublish
```

The CI script builds the application, runs four offline suites and checks static WPF contracts. It does not use a real MediaMonkey database, contact Zidoo or write real playlists. The manually triggered GitHub workflow is a draft and has not run on GitHub.

For the full desktop suite on an interactive Windows desktop:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-MediaMonkeyZidooSync.ps1 -UiDeep -ResultsDirectory "$env:TEMP\MediaMonkeyZidooSync-FullRegression"
```

Desktop startup tests can read existing per-user configuration and update UI state. Use a disposable account or Windows Sandbox for clean first-run acceptance. The deep configuration and recovery tests use disposable fixtures. The Help workflow uses an in-process WPF menu event instead of the older inaccessible desktop-popup lookup. Neutral settings are rejected as incomplete before persistence tests populate synthetic offline values.

## Settings and installation

Packaged device addresses, MAC address, NAS paths, database, playlist and report-location defaults are blank. Existing per-user settings take precedence; valid legacy settings may migrate. Complete deployment settings before validation can succeed. Do not commit local settings, database copies, playlist exports or diagnostic logs.

The draft CI publish step creates an unsigned, self-contained Windows x64 payload. Packaging/Build-NeutralPackage.ps1 wraps a version-matching published payload with the per-user PowerShell installer. Rebuilt artifacts need their own acceptance; earlier binary ZIP hashes do not authenticate a new build. See [Packaging/README.md](Packaging/README.md) for install and removal details.

## Project policies

- [Privacy](PRIVACY.md)
- [Contributing](CONTRIBUTING.md)
- [Security reporting](SECURITY.md)
- [Code signing policy](CODE-SIGNING.md)
- [Third-party components](THIRD-PARTY-NOTICES.md)

No GitHub repository, release, signing service or sponsor is claimed by this candidate. MediaMonkey and Zidoo names identify interoperability targets; no affiliation or endorsement is claimed.
