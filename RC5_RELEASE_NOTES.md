# MM Zidoo Sync 1.1.0-rc.5

Application 1.1.0-rc.5; Windows Installer revision 1.1.5. Owner acceptance remains pending.

- Browse shared music and playlist folders to fill the host, share and folder names. The playlist chooser requires a folder inside the music folder. Saving identical music/playlist paths keeps synchronization disabled.
- Choose a MediaMonkey parent group from its read-only database and review recursive child counts. Help includes a MediaMonkey screenshot and a folder diagram with simple setup steps.
- Load previous configuration JSON from the current settings folder or any saved location; review and save before synchronizing. The original JSON stays unchanged. Help and CONFIGURATION_JSON_REFERENCE.md document settings, UI state, history and recovery JSON.
- Remove the Open Setup Log button. Use Run History / Logs for setup logs.
- Delete selected/all indexed history logs after confirmation. Keep the newest 100 entries per history folder by default; configure another count or 0 to keep all. Cleanup preserves settings, music, playlists, exporter reports and recovery data.
- Restore expanded/collapsed Run History and Recovery Diagnostics sections. Closing the main window closes Help. Output updates respect the reader's scroll position and stop redrawing during thumb dragging; grid selection handlers ignore scrollbars.
- Keep persistent AppData storage and the existing selectable Report / Log Root. Temp content may disappear during Windows cleanup.
- Retain the native MSI title-bar and system Preparing to remove icons. MSI authoring supplies no per-dialog title-bar icon setting; the app icon remains in Programs and Features and inside custom setup pages. This limitation does not affect installation/uninstallation.

The native exporter compares MediaMonkey with M3U files in the selected Windows folder, not a live Zidoo inventory. A folder containing no M3U files can propose every managed playlist as Create. Verify the playlist folder before approving changes. Automated tests never execute live device changes.
