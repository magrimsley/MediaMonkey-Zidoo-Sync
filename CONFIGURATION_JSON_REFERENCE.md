# Configuration JSON reference

Use Load Configuration in Configuration to browse saved settings; review and save before using them. The picker starts at the per-user current configuration folder. Import preserves the original JSON.

The application stores the current file at `%LOCALAPPDATA%\MediaMonkey Zidoo Sync\Configuration\Current\MediaMonkey-Zidoo-Config-v1.0.0.json`. Archive and LastKnownGood keep copies. The v1.0.0 suffix identifies the compatibility format, not the application build. Ordinary upgrades and uninstall preserve preferences.

| JSON section | Fields and meaning |
|---|---|
| Application | Version: compatibility label. ReportRoot: output folder. HistoryKeepCount: recent indexed run entries to keep in each history folder; default 100, 0 keeps all. |
| Zidoo | IPAddress: player address. ApiPort: API port. ExpectedModel: expected player model. MacAddress: wired Ethernet MAC. BroadcastAddress: WOL subnet broadcast. WolPort: WOL port. PlaylistGridColumns: playlist tile columns. |
| NAS | Host: storage computer. ShareName: shared folder. MusicFolderName: music folder inside the share. PlaylistFolderName: playlist folder inside music. MusicRootUNC and PlaylistFolderUNC: complete Windows network paths. |
| MediaMonkey | DatabasePath: MediaMonkey database. PlaylistParent: exact unique parent playlist name whose descendants the application manages. |
| Timing | ZidooReadinessTimeoutSeconds: startup wait limit. ColdBootUiStabilizationSeconds: extra UI startup wait. |

Save Configuration permits incomplete drafts. The application enables synchronization only after strict validation of a complete saved configuration. Blank numeric fields become zero in a draft; fill required positive numbers before running.

`ui-state.json` stores presentation preferences separately. RunHistory JSON indexes an operation log; it is not a configuration. Pending transaction JSON and recovery receipts protect interrupted work. History cleanup removes indexed log/index pairs only and leaves settings, exporter output, playlist files and recovery data in place.

AppData provides persistent per-user storage. Choose another Report / Log Root if desired; Windows can remove Temp content. History entries to keep bounds indexed run history. Delete Selected Run and Delete All History request confirmation and remove logs without changing music or settings.

Windows Installer owns its title-bar and Preparing to remove icons. Native MSI Dialog authoring provides no per-dialog title-bar icon field; ARPPRODUCTICON brands Programs and Features. The custom setup pages display app artwork inside their content area. Changing the system-owned window icons requires a different external setup interface; this MSI preserves native Windows installation/uninstallation behavior.

Load Configuration opens any saved configuration for review; Save Configuration activates the reviewed values. Restore Selected Archive first archives the current settings, then restores the selected internal snapshot.
