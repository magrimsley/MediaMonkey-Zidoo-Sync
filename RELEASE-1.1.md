# MM Zidoo Sync 1.1 — RC10 promoted to stable

Production application version 1.1.0 (display name 1.1), FileVersion 1.1.0.11, MSI ProductVersion 1.1.11. The numeric MSI revision remains greater than RC10's 1.1.10 so Windows Installer can upgrade it. The stable release is v1.1.0 and is not a GitHub prerelease.

The owner authorized production promotion of RC10. This release retains RC10 synchronization behavior; promotion changes version identifiers and project/release organization, not playlist rules. The separate forced-reimport feature remains commented out and deferred in the backlog. Review offers Back and Use Reviewed Plan and follows Windows light/dark themes. Help explains that approval precedes Run Synchronization.

The non-dot shared-storage temporary-file correction and visibility/recovery verification remain. Default CREATE, UPDATE, DELETE, exact-byte comparison and no-change/no-device-contact behavior follow the frozen process. Frozen PowerShell references are unchanged.

Source, tests, documentation and backlogs are shared by Visual Studio and GitHub. Preserved diagnostics, historical installers and frozen references are visible in the ignored local solution under the project folder structure. See Documentation/PROJECT-FILES.md. They are not included in the public source or production package.

Release validation uses isolated files and simulated devices, including Windows dark/light UI checks, file visibility and durable recovery checks, native process-kill/restart scenarios, full payload hash checks and an isolated MSI lifecycle. Those checks do not establish new live-device acceptance. The owner authorized promotion; no new live synchronization was run during promotion.

The installer is unsigned. It preserves configuration, archives, preferences, diagnostic logs and recovery evidence during normal upgrades/uninstall. Detailed JSON documentation and legal/dependency/artwork notices remain packaged.

Retained known limitation: device reconciliation verifies playlist identity/counts, not complete remote ordered-track equivalence. A matching M3U alone cannot detect edits made directly in Zidoo. See KNOWN-BUGS.md for previously recorded intermittent file-sharing behavior and unresolved diagnostic limits.
