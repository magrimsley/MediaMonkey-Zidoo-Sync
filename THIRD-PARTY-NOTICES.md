# Third-party component review

dependency-inventory.json records the seven NuGet packages resolved for the existing .NET 9 application build. Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core declare MIT; the four SQLitePCLRaw packages declare Apache-2.0. System.Memory 4.5.3 uses legacy license metadata requiring upstream review. These declarations were read from cached package metadata, not assigned by this project.

This inventory is not a complete license-notice bundle. Before distributing source-built binaries, collect and retain upstream license/NOTICE texts, review the embedded native SQLite component and the .NET runtime included by self-contained publication, and confirm redistribution requirements for the exact versions being shipped. No ownership or re-licensing of upstream components is claimed.

The project's own license remains undecided; see LICENSE-DECISION.md.
