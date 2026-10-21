# MM Zidoo Sync 1.1.0-rc.8

Application 1.1.0-rc.8; MSI 1.1.8. This candidate supersedes RC7. Owner acceptance remains pending.

Synchronization now captures recovery backups for existing physical transaction targets, including hidden files excluded from the frozen visible-file planning baseline. It verifies the reviewed target hash before capturing the backup. The frozen SHA-256 planning rules remain unchanged. CREATE retains an existing matching device playlist even when a hidden physical target already exists; it does not reinterpret that CREATE as a destructive UPDATE.

Synchronization displays the actual validation failure rather than suggesting configuration caused every InvalidDataException. Directory errors identify the folder, share and number of matches. When a hostname path is unavailable on Zidoo, the application resolves that hostname through Windows and accepts only one resolved IPv4 address whose share and configured folders pass the same checks. It preserves saved names, file ordering and the frozen navigation sequence. Missing or ambiguous results stop before playlist mutations. Live hostname behavior still needs owner acceptance; fixture tests use fake HTTP and DNS.

Saving stays on Configuration. Load Configuration appears last in the action row and opens previous settings for review. Configuration Help explains it and distinguishes archive restoration. Help uses a generic playlist-folder instruction; the separate JSON topic moves out of the Help navigation, while CONFIGURATION_JSON_REFERENCE.md remains packaged. Normal upgrade/uninstall preference retention remains unchanged; clean removal remains a one-time test reset.

Validation: 107 isolated UI/configuration checks; fake-device hostname, hidden-file commit/rollback and CREATE identity checks; existing native transaction and process-kill recovery tests; 21 isolated synchronization checks. No live device commands or owner music/playlist changes during development.
