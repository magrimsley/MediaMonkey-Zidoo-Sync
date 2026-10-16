# MediaMonkey Zidoo Sync unsigned test package

Application Alpha.60.17.1; installer support revision 5. Generated with neutral defaults. Fresh package acceptance is pending; a successful build/hash check alone is not regression acceptance. Native production synchronization is enabled in this candidate; complete acceptance is pending. No signing or public publication is performed.

Extract the whole ZIP to a local folder. Open Windows PowerShell in that folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-Package.ps1
```

Installation is per-user, verifies package hashes, and does not launch the app. Personal deployment fields start blank unless existing per-user settings or valid legacy settings take precedence. Blank fields must be completed before configuration validation can succeed.

Keep the complete extracted package for interrupted-uninstall recovery. From its directory run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Installer\Uninstall.ps1 -InstallRoot "$env:LOCALAPPDATA\Programs\MediaMonkey Zidoo Sync"
```

Configuration, archives, unknown files and recovery evidence are retained. The installation directory may remain. Hashes detect changes but do not authenticate a publisher. This package is unsigned.
