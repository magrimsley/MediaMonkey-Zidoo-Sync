# MM Zidoo Sync 1.1.0-rc.4

Installer revision: 1.1.4. This candidate supersedes RC3 for owner acceptance testing.

- Setup displays application artwork, native progress, and a completion, cancellation, or failure page. Desktop shortcut choice remains optional.
- Save Configuration preserves incomplete setup drafts and archives the previous settings. Synchronization still requires a complete valid saved configuration.
- Configuration and Run History / Logs stay available during incomplete setup. Startup selects Configuration for an incomplete setup and Synchronization for a complete setup.
- Setup saves and failures create local history logs. Configuration shows the exact path and provides Open Setup Log; invalid numeric input identifies its field.
- Help uses active voice and explains Playlist Parent with MediaMonkey examples, storage host/share/folder names with consistent UNC examples, partial saving, and diagnostic access.
- End-user Help omits the Chinese developer API reference and keeps the illustrated MAC-address setup guide. The application does not control Windows' Start menu New label or the language a third-party website serves.

The application retains the RC3 discovery, browse, installed icon, log selection, and preference-preserving Windows Installer behavior. Upgrades and ordinary uninstall preserve user configuration; synchronization and Last Known Good still use strict validation.

No live Zidoo synchronization forms part of this candidate's automated checks. Owner acceptance remains pending. MSI and tooling stay local; source changes remain on the draft review branch until release approval.
