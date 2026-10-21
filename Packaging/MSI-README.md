# MSI installer — release candidate

This native Windows Installer package uses no PowerShell, custom actions or paid installer-tool dependency. Build on 64-bit Windows after publishing the application:

    dotnet run --project Packaging/MsiBuilder/MsiBuilder.csproj -c Release -- <payload> <output.msi> 1.1.2

The builder uses the Windows Installer automation API and makecab.exe included with Windows. It embeds the self-contained payload and preserves its complete GPL/dependency/artwork notices. MSI output and cabinet intermediates belong outside the payload and must not be committed.

The MSI is per-user and installs under LocalAppData/Programs/MMZidooSync. Settings and layout continue to use LocalAppData/MediaMonkey Zidoo Sync, exactly as the earlier releases. These user-data files are not MSI components: install, upgrade and uninstall must preserve them. No database/NAS/device operation is part of installation.

During transition from the earlier local PowerShell installer, MSI installs alongside that installation instead of taking ownership of its files or uninstall record. The MSI desktop shortcut is MM Zidoo Sync (MSI); its Start menu entry is inside the MM Zidoo Sync folder. Existing custom-installer shortcuts and fallback versions remain. Do not manually delete the earlier installation or its uninstall metadata. Portable distribution remains available.

Windows Installer uses only three numeric version fields. Application 1.1.0-rc.2 uses internal MSI revision 1.1.2. Keep this internal sequence increasing for subsequent installers, including stable promotion; do not reset it to application 1.1.0. Each differing MSI has a new package code; product code changes with MSI revision and UpgradeCode stays stable. The application itself displays the semantic version, not this internal MSI sequence.

The upgrade installs new components transactionally before removing the old product; downgrade detection rejects an older/equal different product. Failure, rollback, upgrade, downgrade and file-preserving uninstall require validation before distribution. The installer uses basic Windows progress UI. It is unsigned while SignPath approval is pending.

Build output is not proof of acceptance. Record actual tests and limitations in the candidate release notes.

File-table versions use numeric fixed-file metadata, matching Windows Installer, rather than free-form display strings. Each field must fit 0–65535. For example, msquic.dll displays 2.5.10.154561281 but its fixed version is 2.5.10.0. Full-payload acceptance is necessary because a tiny fixture cannot detect this metadata defect.

Unreleased installer follow-up: full interactive installation offers a Create a desktop shortcut checkbox. The Start menu entry is independent. For silent/basic installation use DESKTOPSHORTCUT=0 to omit the desktop shortcut or DESKTOPSHORTCUT=1 to create it (default). NOSHORTCUTS=1 remains an isolated-test option. Full interactive visual acceptance is still pending.

RC3 uses application version 1.1.0-rc.3, FileVersion 1.1.0.3, and numeric MSI revision 1.1.3. Startup ignores the last open tab: incomplete settings select Configuration and disable every other tab; saved complete settings select Synchronization.
