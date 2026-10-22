# Project files in Visual Studio and GitHub

The public source solution is MediaMonkeyZidooSync/MediaMonkeyZidooSync.sln. Application source, tests, notices, release documentation and both backlog documents are committed to Git. A clean checkout of the release tag reproduces that source state.

The local solution is MediaMonkeyZidooSync/MediaMonkeyZidooSync.Local.sln. It uses the same application project and adds the local archive and frozen references as Solution Items. These are visible in Visual Studio but are not compiled or packaged into the application.

Preserved session content lives under LocalOnly/SessionArchive. Its SOURCE-TO-ARCHIVE.csv records original paths, preserved paths, SHA256 values and source timestamps. Identical content is stored once. The local solution exposes all preserved files and the archive index. Old tools can contain retired absolute paths; consult the mapping before reuse.

Frozen authoritative references remain under SourceReference/Production-v1.0.0 and must match SOURCE-SHA256.json. They are not modified by consolidation or release promotion.

Private diagnostics, frozen PowerShell, historical source packages and installer fixtures remain local. They must not be committed to the public repository. Public source and release metadata match GitHub; local historical evidence deliberately does not form part of a public checkout. Production MSI assets and checksums are attached to the stable GitHub release instead of being committed as source.

Cleanup removes only specifically inventoried source files whose preserved content has been independently verified. Files that changed, new files outside the snapshot, active installed-product source packages and final user-facing reports are retained. No live application data, MediaMonkey database or NAS files belong to this cleanup.

The [C# source index](SOURCE-INDEX.md) inventories production source, offline test projects and manual/legacy tools. The public solution exposes these sources as Solution Items while building only the application project. Historical installer dependencies must be checked after each install/uninstall: RC10 original files were removed after uninstall, and Windows now references the production MSI at its original download location. Keep the current production source installer available.
