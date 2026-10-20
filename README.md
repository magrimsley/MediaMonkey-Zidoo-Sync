# MM Zidoo Sync

Windows WPF application, version **1.1-beta.1**, for synchronizing managed MediaMonkey playlists with NAS M3U files and persistent Zidoo Music playlists. The application and synchronization engine are entirely native C#. PowerShell supports build, testing and installation only.

Follow **Validate MediaMonkey → Verify Native Exporter → Review Changes → Run Synchronization**. Configuration changes and completed, cancelled or failed runs require the checks again. Errors are presented in plain language; detailed exceptions are retained in diagnostic logs. Successful verified synchronization is green, intentional cancellation is yellow. Appearance follows the Windows color scheme.

Interrupted runs are reconciled through this same sequence. Before applying new changes, Run Synchronization verifies the retained transaction and its recorded device/power ownership. A verified completed change is kept; an incomplete change is restored. Recovery ends that attempt so the current plan must be validated and reviewed again. Unexpected external edits, changed device settings, invalid evidence or an unverifiable device response preserve the recovery gate and stop synchronization. See [architecture and recovery](ARCHITECTURE.md).

The repository is private and the package is unsigned. Project-owned source is licensed under GPL-3.0-only; see [LICENSE](LICENSE). [Voluntary donations](DONATIONS.md) are welcome. No signing sponsorship is implied. Acceptance is specific to the source commit, package hashes, tested device and recorded scenarios. Release notes record actual results; a build alone is not production acceptance.

## Build and offline tests

Use Windows and the .NET 9 SDK. Local Visual Studio installer/test scripts are excluded from GitHub. Open `MediaMonkeyZidooSync/MediaMonkeyZidooSync.sln` in Visual Studio, or run:

```powershell
dotnet build .\MediaMonkeyZidooSync\MediaMonkeyZidooSync.csproj -c Release
dotnet run --project .\Testing\RepositoryPolicyChecks\RepositoryPolicyChecks.csproj -c Release -- .
dotnet run --project .\NativeDeviceTests\NativeDeviceTests.csproj -c Release
```

The offline suites use simulated devices and disposable files, including real termination/restart of child test processes. They do not contact Zidoo or alter real playlists. Desktop smoke tests can read per-user settings and save UI state; use Windows Sandbox or a disposable account for clean-profile acceptance. Explicit live tests are separate and must be bound to their reviewed device, isolated database, owned NAS folder and test playlist.

See [current status](CURRENT-STATUS.md), [testing](AUTOMATED_TESTING_README.md) and [repository policy](REPOSITORY-POLICY.md) for current acceptance and local-only tooling scope.

## Settings and installation

Device addresses, MAC address, NAS paths, database and report defaults are blank in distributed packages. Existing per-user settings take precedence; valid legacy settings may migrate. Complete and save configuration before validation. Never commit personal settings, database copies, exported playlists or diagnostic logs.

In the local Visual Studio checkout only, `Packaging/Build-NeutralPackage.ps1` wraps a version-matching, self-contained Windows x64 payload with the per-user installer. These local PowerShell-bearing packages must not be uploaded to GitHub. Each artifact has a manifest and SHA256 sidecar. Hashes detect changes and do not authenticate a publisher. Keep the complete extracted package for interrupted-uninstall recovery. Configuration, archives, unknown files and recovery evidence are retained during removal. See [packaging](Packaging/README.md).

## Known bugs

See [known bugs](KNOWN-BUGS.md), including the intermittent transaction-file sharing failure whose cause remains unidentified. It did not recur during the latest diagnostic capture; earlier offline tests did reproduce it.

## Project policies

- [License decision](LICENSE-DECISION.md)
- [Privacy](PRIVACY.md)
- [Contributing](CONTRIBUTING.md)
- [Security reporting](SECURITY.md)
- [Code signing](CODE-SIGNING.md)
- [Third-party components](THIRD-PARTY-NOTICES.md)

MediaMonkey and Zidoo names identify interoperability targets; no affiliation or endorsement is claimed.
Artwork origin and permissions: [artwork record](ARTWORK-PROVENANCE.md). Preserve the ThirdPartyNotices directory with redistributed builds.
