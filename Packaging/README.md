# Packaging sources

Installer support revision 5 with neutral-default packaging tools. These are solution items, not application compilation inputs. Build-NeutralPackage.ps1 takes a published Alpha.60.17.1 PayloadDirectory and writes a new unsigned ZIP, file manifest and SHA256 sidecar outside the source tree. It validates version rather than requiring the original private checkpoint executable hash: every source rebuild requires fresh acceptance.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Packaging\Build-NeutralPackage.ps1 -PayloadDirectory 'C:\Temp\MyPublishedPayload' -ResultsRoot 'C:\Temp\MyPackages'
```

Extract a generated ZIP and run Install-Package.ps1 in Windows PowerShell. Installation is per-user and does not launch the app. Keep the extracted package available to run Installer/Uninstall.ps1 with the original InstallRoot if removal is interrupted. Configuration, archives, unknown files and recovery evidence remain after removal.

Tests/Test-InstallerFiles.ps1 requires explicit old and new payload directories. Tests/Test-UninstallRecovery.ps1 requires an explicit payload directory. Results default to the operating-system temporary directory; an explicit ResultsRoot can preserve a preferred local testing directory. Do not store generated ZIPs, installed payloads or results in this source folder.

No signing, publication, production sync or certificate sponsorship is claimed. The project license is undecided. See the root README and CODE-SIGNING.md.
