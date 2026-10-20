# Third-party notices — Beta 1 dependency review

Project-owned code uses GPL-3.0-only. Dependencies retain their own licenses. Preserve this document and the complete ThirdPartyNotices directory when redistributing the self-contained application.

| Component | Version | License / notice files |
| --- | --- | --- |
| Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core | 9.0.0 | MIT; Microsoft.Data.Sqlite-LICENSE.txt |
| SQLitePCLRaw.bundle_e_sqlite3, core, lib.e_sqlite3, provider.e_sqlite3 | 2.1.10 | Apache-2.0; SQLitePCLRaw-LICENSE.txt and SQLitePCLRaw-NOTICE.txt; Copyright 2014–2024 SourceGear, LLC |
| System.Memory | 4.5.3 | MIT; System.Memory-LICENSE.txt and System.Memory-THIRD-PARTY-NOTICES.txt |
| Microsoft.NETCore.App.Runtime.win-x64 | 9.0.20 | MIT plus upstream component notices; NET-runtime-LICENSE.txt and NET-runtime-THIRD-PARTY-NOTICES.txt |
| Microsoft.WindowsDesktop.App.Runtime.win-x64 | 9.0.20 | MIT; WindowsDesktop-runtime-LICENSE.txt, WPF and Windows Forms license/notice files |
| SQLite native engine bundled by SQLitePCLRaw | as bundled in lib.e_sqlite3 2.1.10 | Public-domain dedication; SQLite-public-domain.html. Package wrapper/build material retains its Apache terms. |

System.Memory is a resolved dependency; the netcoreapp2.1 placeholder means the .NET 9 runtime supplies that API rather than copying the old netstandard assembly. Its upstream notices are retained conservatively. The runtime notice bundles contain additional licenses; they are not replaced by the project's GPL.

Sources: exact cached NuGet packages for the stated versions; EF Core source commit 645f3131a5b0a4bf677201cf22773990a5316c89; SQLitePCL.raw v2.1.10; dotnet/wpf and dotnet/winforms v9.0.20; https://www.sqlite.org/copyright.html. See ThirdPartyNotices/manifest.json for file hashes. MIT and Apache-2.0 permit use with GPLv3 while retaining upstream notices (https://www.apache.org/licenses/GPL-compatibility).

This review covers the identified Beta 1 Windows x64 dependency set, not future dependency updates or a blanket legal clearance. The SQLitePCLRaw upstream NOTICE.TXT is retained, including its native build attributions. No separate NOTICE file was identified at the reviewed EF Core source root; runtime notice bundles remain intact. Recheck notices when dependencies or publication targets change.

The original Beta 1 ZIP omitted these full texts. A separate notice supplement accompanies that unchanged tested binary; future publishes copy them automatically. Artwork provenance and the limits of the owner's grant are recorded in ARTWORK-PROVENANCE.md.
