# MM Zidoo Sync 1.1.0-rc.3 — unsigned test candidate

This candidate includes the first-install corrections missing from RC2:
- No saved complete configuration: select Configuration and disable every other tab. Saved complete configuration: always select Synchronization, regardless of the last open tab.
- Native installer desktop-shortcut checkbox, independent Start menu entry, and the application icon in Programs and Features.
- Visible selected-log cell highlight that persists while opening its log.
- Discovery records success, no-device and error outcomes, including before a report folder is configured. Discovery fills the WOL broadcast using the matching local subnet mask, or clearly requires manual entry if it cannot determine one safely.
- Browse controls for database, music, playlist and log locations; corrected Help and default MediaMonkey database path.

Application version 1.1.0-rc.3; Windows file version 1.1.0.3; internal numeric MSI revision 1.1.3. Preferences/configuration remain outside MSI ownership. Uninstall preserves user data; this candidate does not implement an erase-user-data option. Unsaved-edit confirmation and history retention remain backlog work.

Packages remain unsigned. KB-001 remains unresolved and documented. No live synchronization or new Sandbox acceptance is claimed. This is a prepared local test candidate; public release/promotion remains a separate owner decision. The MSI replaces an earlier MSI through its native upgrade path; migration from the older custom installer remains separate work.
