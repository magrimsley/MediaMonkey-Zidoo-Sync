# MM Zidoo Sync testing — production 1.1

The console test projects use dotnet run, rather than dotnet test adapters.

From the repository root:

```text
dotnet build MediaMonkeyZidooSync/MediaMonkeyZidooSync.csproj -c Release
dotnet run --project Testing/RepositoryPolicyChecks/RepositoryPolicyChecks.csproj -c Release -- .
dotnet run --project NativeOrchestrationTests/NativeOrchestrationTests.csproj -c Release
dotnet run --project RecoveryTests/RecoveryTests.csproj -c Release
dotnet run --project RunHistoryDurabilityTests/RunHistoryDurabilityTests.csproj -c Release
dotnet run --project IsolatedSyncTests/IsolatedSyncTests.csproj -c Release
dotnet run --project NativeDeviceTests/NativeDeviceTests.csproj -c Release
```

Run stages serially. The workflow also publishes a self-contained unsigned Windows x64 build, without uploading or releasing it. The native offline projects use disposable files and simulated devices; explicit live probes are separate and must not be enabled casually.

The local Visual Studio checkout retains PowerShell orchestration, installer, static-contract, desktop and Sandbox tooling. These files are deliberately absent from GitHub. GitHub CI does not claim those local-only checks ran.

Interactive checks require a desktop and must run separately:

```text
dotnet run --project Testing/NeutralSafeWorkflowTests/NeutralSafeWorkflowTests.csproj -c Release
dotnet run --project WpfWorkflowTests/WpfWorkflowTests.csproj -c Release
```

They briefly display test windows. The isolated main windows use disposable configuration roots. RepositoryPolicyChecks inspects tracked files, not every ignored file on the local workstation.

See CURRENT-STATUS.md and KNOWN-BUGS.md for actual acceptance and the unresolved backend sharing failures. A build, passing focused UI checks or a successful rerun does not establish full release acceptance.

## Source and legacy tooling

See [the complete C# source/project index](Documentation/SOURCE-INDEX.md) for all projects and source files. NativeProductionRunner and InstalledWorkflowAcceptance are live-capable/manual utilities, not offline CI entry points. InstalledWorkflowAcceptance still targets a retired assembly filename; MigrationTests requires historical preparation/application source links. Neither standalone legacy project is established as production-ready by the current CI. Adapt and validate them before reuse.
