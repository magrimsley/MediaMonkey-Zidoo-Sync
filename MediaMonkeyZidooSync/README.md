# MM Zidoo Sync

Native Windows WPF application, **1.1-beta.1**. Executable: **MMZidooSync.exe**.

**MediaMonkey → Zidoo playlist synchronization**

MediaMonkey is the source of truth. The required flow remains Validate MediaMonkey, Verify Native Exporter, Review Changes, Run Synchronization. The optional diagnostics controls were removed and their handlers commented out. About/Help display the supplied artwork and approved description; the startup splash closes two seconds after the main UI first renders.

Settings and installation ownership retain legacy directory/key names for continuity. Do not rename those identifiers merely to match the display name.

See ../CURRENT-STATUS.md, ../KNOWN-BUGS.md, ../AUTOMATED_TESTING_README.md and ../REPOSITORY-POLICY.md for current acceptance and distribution policy. Historical Alpha notes do not describe the current Beta state.
