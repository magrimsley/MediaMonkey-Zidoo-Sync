# Alpha60.18.8 changes

- Removed Optional read-only diagnostics from the interface and commented out its assessment/inspection handlers. The required Validate MediaMonkey, Verify Native Exporter, Review Changes, Run Synchronization sequence remains.
- Added product-owner-supplied application artwork as a multi-resolution executable/window/notification-area icon. The installer creates desktop and Start menu shortcuts with that executable icon; ownership checks protect unrelated shortcuts. Uninstall handles both owned shortcut locations.
- Added the second supplied image to startup splash and Help. Double-clicking the tray icon restores the main window; closing the application removes its tray icon. Existing close/minimize behavior remains.
- MediaMonkey remains the source of truth. No Pending Changes tab or reverse Zidoo synchronization was added.
- KB-001 remains open: intermittent transaction evidence-file sharing failure; cause unidentified. Passing tests do not resolve it.

The supplied JPEG originals remain retained. Icon conversion frames the central symbol in a square and provides sizes 16 through 256 pixels. No artwork was regenerated.

## Verification and local delivery

The exact packaged DLL passed 28 isolated UI/configuration checks, including native notification-area icon registration and cleanup. All nine isolated installer/upgrade/file-preservation checks passed. Startup, tab navigation and the Help routed-menu workflow passed. All 38 immutable PowerShell baseline hashes match.

Full regression is not green: a later offline transaction run encountered a sharing exception while saving the pending marker. Earlier runs passed the transaction stage but exposed stale UI test assumptions, which were corrected for the removed controls and new splash window. All original run logs remain preserved. KB-001 records the additional observation without claiming a common root cause.

Installed locally as 1.1-alpha.60.18.8 alongside retained 18.7 and earlier versions. Installed DLL matches the package; existing configuration/archive hashes remain unchanged. Both desktop and Start menu shortcuts target 18.8 and its executable icon. No live Zidoo operations, GitHub push, CI run or prerelease publication occurred for 18.8.