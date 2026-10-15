# Alpha.41 isolated recovery assessment tests

Run on the Windows development computer with .NET 9 SDK:

```powershell
dotnet run --project .\RecoveryTests\RecoveryTests.csproj -c Debug
```

These tests compile the **existing** alpha.41 journal and recovery coordinator source files directly (no copies), and exercise six states, missing journal, repeated assessment, malformed journal, invalid ID, unknown state, duplicate IDs, and incomplete filtering. They create unique temporary directories under the operating system temp path and remove them afterward. They do not use E:\Temp\MediaMonkey, authoritative playlist files, Zidoo, or the working .bat/.ps1 scripts. No rollback or remote operations are executed.

Expected result: `Results: 13 passed; 0 failed`. This has **not** been run in the authoring environment because the .NET SDK is unavailable there. Run the command above and provide the output.

Alpha.42 adds seven isolated rollback tests; expected total: 20.

Alpha.43 adds seven sandbox-only journal integration tests, for 27 total.

Alpha.44 adds five evidence store tests (32 total expected). Run `dotnet run --project .\RecoveryTests\RecoveryTests.csproj -c Debug` from package root.

Alpha.46 expected: 44 passed / 0 failed; run on Windows for verification.
