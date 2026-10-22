# C# source and project index

This index covers all 93 tracked C# files and 13 C# project definitions. It describes source organization and build membership; it is not a claim that every method has complete XML/API documentation. Generated bin/obj files, archived source snapshots and local evidence are not production source.

Production behavior and recovery contracts: [architecture](../ARCHITECTURE.md). Build and test entry points: [testing guide](../AUTOMATED_TESTING_README.md). Local/private evidence: [project files](PROJECT-FILES.md).

The public solution builds the application. Other source/project files appear as Solution Items for navigation; listing a test project file does not build or execute that project. Run supported test projects explicitly as documented. Legacy/live utilities below are not automatically validated by the production offline CI.

## IsolatedSyncTests

Project: [IsolatedSyncTests.csproj](../IsolatedSyncTests/IsolatedSyncTests.csproj) — `net9.0`. Disposable-file isolated synchronization checks; links isolated synchronization service.

| C# file | Source category |
| --- | --- |
| [Program.cs](../IsolatedSyncTests/Program.cs) | Test/tool source |

## MediaMonkeyZidooSync

Project: [MediaMonkeyZidooSync.csproj](../MediaMonkeyZidooSync/MediaMonkeyZidooSync.csproj) — `net9.0-windows`. Production WPF application. SDK default Compile includes its C# files; XAML generates presentation bindings.

| C# file | Source category |
| --- | --- |
| [AboutWindow.cs](../MediaMonkeyZidooSync/AboutWindow.cs) | Application UI/startup |
| [App.xaml.cs](../MediaMonkeyZidooSync/App.xaml.cs) | Application UI/startup |
| [ApplicationBranding.cs](../MediaMonkeyZidooSync/ApplicationBranding.cs) | Application UI/startup |
| [HelpWindow.xaml.cs](../MediaMonkeyZidooSync/HelpWindow.xaml.cs) | Application UI/startup |
| [MainWindow.xaml.cs](../MediaMonkeyZidooSync/MainWindow.xaml.cs) | Application UI/startup |
| [Models/AppConfig.cs](../MediaMonkeyZidooSync/Models/AppConfig.cs) | Application model |
| [Models/LogEvent.cs](../MediaMonkeyZidooSync/Models/LogEvent.cs) | Application model |
| [Models/MediaMonkeyPlaylistExportModels.cs](../MediaMonkeyZidooSync/Models/MediaMonkeyPlaylistExportModels.cs) | Application model |
| [Models/UiState.cs](../MediaMonkeyZidooSync/Models/UiState.cs) | Application model |
| [Services/ApplicationVersionProvider.cs](../MediaMonkeyZidooSync/Services/ApplicationVersionProvider.cs) | Application service |
| [Services/ConfigurationService.cs](../MediaMonkeyZidooSync/Services/ConfigurationService.cs) | Application service |
| [Services/DiagnosticReportingService.cs](../MediaMonkeyZidooSync/Services/DiagnosticReportingService.cs) | Application service |
| [Services/FrozenM3uContent.cs](../MediaMonkeyZidooSync/Services/FrozenM3uContent.cs) | Application service |
| [Services/FrozenPlaylistFiles.cs](../MediaMonkeyZidooSync/Services/FrozenPlaylistFiles.cs) | Application service |
| [Services/GridSelectionBehavior.cs](../MediaMonkeyZidooSync/Services/GridSelectionBehavior.cs) | Application service |
| [Services/INativeSyncPlanProvider.cs](../MediaMonkeyZidooSync/Services/INativeSyncPlanProvider.cs) | Application service |
| [Services/IsolatedJournalRecoveryCoordinator.cs](../MediaMonkeyZidooSync/Services/IsolatedJournalRecoveryCoordinator.cs) | Application service |
| [Services/IsolatedJournalSequenceAuditService.cs](../MediaMonkeyZidooSync/Services/IsolatedJournalSequenceAuditService.cs) | Application service |
| [Services/IsolatedPlaylistRollbackService.cs](../MediaMonkeyZidooSync/Services/IsolatedPlaylistRollbackService.cs) | Application service |
| [Services/IsolatedPlaylistSyncService.cs](../MediaMonkeyZidooSync/Services/IsolatedPlaylistSyncService.cs) | Application service |
| [Services/IsolatedRecoveryAuthorizationService.cs](../MediaMonkeyZidooSync/Services/IsolatedRecoveryAuthorizationService.cs) | Application service |
| [Services/IsolatedRecoveryDiagnosticsService.cs](../MediaMonkeyZidooSync/Services/IsolatedRecoveryDiagnosticsService.cs) | Application service |
| [Services/IsolatedRecoveryEvidenceStore.cs](../MediaMonkeyZidooSync/Services/IsolatedRecoveryEvidenceStore.cs) | Application service |
| [Services/IsolatedRecoveryExecutionPlanner.cs](../MediaMonkeyZidooSync/Services/IsolatedRecoveryExecutionPlanner.cs) | Application service |
| [Services/IsolatedRecoveryPlanReportStore.cs](../MediaMonkeyZidooSync/Services/IsolatedRecoveryPlanReportStore.cs) | Application service |
| [Services/IsolatedRecoveryReportBrowserService.cs](../MediaMonkeyZidooSync/Services/IsolatedRecoveryReportBrowserService.cs) | Application service |
| [Services/IsolatedRestartReconciliationService.cs](../MediaMonkeyZidooSync/Services/IsolatedRestartReconciliationService.cs) | Application service |
| [Services/IsolatedRestartSafetyAuditService.cs](../MediaMonkeyZidooSync/Services/IsolatedRestartSafetyAuditService.cs) | Application service |
| [Services/IsolatedStartupMode.cs](../MediaMonkeyZidooSync/Services/IsolatedStartupMode.cs) | Application service |
| [Services/IsolatedTransactionSequenceStore.cs](../MediaMonkeyZidooSync/Services/IsolatedTransactionSequenceStore.cs) | Application service |
| [Services/MediaMonkeyExportTransactionPlanner.cs](../MediaMonkeyZidooSync/Services/MediaMonkeyExportTransactionPlanner.cs) | Application service |
| [Services/MediaMonkeyExporterEquivalenceService.cs](../MediaMonkeyZidooSync/Services/MediaMonkeyExporterEquivalenceService.cs) | Application service |
| [Services/MediaMonkeyPlaylistExporter.cs](../MediaMonkeyZidooSync/Services/MediaMonkeyPlaylistExporter.cs) | Application service |
| [Services/NativeInterruptedRecovery.cs](../MediaMonkeyZidooSync/Services/NativeInterruptedRecovery.cs) | Application service |
| [Services/NativePlanInspection.cs](../MediaMonkeyZidooSync/Services/NativePlanInspection.cs) | Application service |
| [Services/NativePlaylistFolderReconciler.cs](../MediaMonkeyZidooSync/Services/NativePlaylistFolderReconciler.cs) | Application service |
| [Services/NativePlaylistStagingCoordinator.cs](../MediaMonkeyZidooSync/Services/NativePlaylistStagingCoordinator.cs) | Application service |
| [Services/NativePlaylistTransactionService.cs](../MediaMonkeyZidooSync/Services/NativePlaylistTransactionService.cs) | Application service |
| [Services/NativeProductionRecovery.cs](../MediaMonkeyZidooSync/Services/NativeProductionRecovery.cs) | Application service |
| [Services/NativeProductionRunState.cs](../MediaMonkeyZidooSync/Services/NativeProductionRunState.cs) | Application service |
| [Services/NativeProductionSyncService.cs](../MediaMonkeyZidooSync/Services/NativeProductionSyncService.cs) | Application service |
| [Services/NativeProductionTransaction.cs](../MediaMonkeyZidooSync/Services/NativeProductionTransaction.cs) | Application service |
| [Services/NativeSyncReadOnlyOrchestrator.cs](../MediaMonkeyZidooSync/Services/NativeSyncReadOnlyOrchestrator.cs) | Application service |
| [Services/NativeZidooConnectivityPowerService.cs](../MediaMonkeyZidooSync/Services/NativeZidooConnectivityPowerService.cs) | Application service |
| [Services/NativeZidooMusicPlaylistService.cs](../MediaMonkeyZidooSync/Services/NativeZidooMusicPlaylistService.cs) | Application service |
| [Services/NativeZidooPlaylistDevice.cs](../MediaMonkeyZidooSync/Services/NativeZidooPlaylistDevice.cs) | Application service |
| [Services/NativeZidooReadiness.cs](../MediaMonkeyZidooSync/Services/NativeZidooReadiness.cs) | Application service |
| [Services/PlaylistParentService.cs](../MediaMonkeyZidooSync/Services/PlaylistParentService.cs) | Application service |
| [Services/PlaylistRecoveryCoordinator.cs](../MediaMonkeyZidooSync/Services/PlaylistRecoveryCoordinator.cs) | Application service |
| [Services/PlaylistTransactionJournalService.cs](../MediaMonkeyZidooSync/Services/PlaylistTransactionJournalService.cs) | Application service |
| [Services/PlaylistTransactionPreflight.cs](../MediaMonkeyZidooSync/Services/PlaylistTransactionPreflight.cs) | Application service |
| [Services/RecoveryFileReader.cs](../MediaMonkeyZidooSync/Services/RecoveryFileReader.cs) | Application service |
| [Services/RecoveryReportDisplaySummary.cs](../MediaMonkeyZidooSync/Services/RecoveryReportDisplaySummary.cs) | Application service |
| [Services/RunHistoryBrowserService.cs](../MediaMonkeyZidooSync/Services/RunHistoryBrowserService.cs) | Application service |
| [Services/RunHistoryService.cs](../MediaMonkeyZidooSync/Services/RunHistoryService.cs) | Application service |
| [Services/StorageSetupService.cs](../MediaMonkeyZidooSync/Services/StorageSetupService.cs) | Application service |
| [Services/SyncRunner.cs](../MediaMonkeyZidooSync/Services/SyncRunner.cs) | Application service |
| [Services/SynchronizationWorkflow.cs](../MediaMonkeyZidooSync/Services/SynchronizationWorkflow.cs) | Application service |
| [Services/UiStateService.cs](../MediaMonkeyZidooSync/Services/UiStateService.cs) | Application service |
| [Services/UserMessages.cs](../MediaMonkeyZidooSync/Services/UserMessages.cs) | Application service |
| [Services/WindowsThemeService.cs](../MediaMonkeyZidooSync/Services/WindowsThemeService.cs) | Application service |
| [Services/ZidooDiscoveryService.cs](../MediaMonkeyZidooSync/Services/ZidooDiscoveryService.cs) | Application service |
| [Services/ZidooPlaylistProtocol.cs](../MediaMonkeyZidooSync/Services/ZidooPlaylistProtocol.cs) | Application service |
| [StartupSplash.cs](../MediaMonkeyZidooSync/StartupSplash.cs) | Application UI/startup |
| [ZidooDiscoveryWindow.xaml.cs](../MediaMonkeyZidooSync/ZidooDiscoveryWindow.xaml.cs) | Application UI/startup |

## NativeDeviceTests

Project: [NativeDeviceTests.csproj](../NativeDeviceTests/NativeDeviceTests.csproj) — `net9.0`. Offline simulated device, transaction, cancellation and process-restart checks; explicitly links selected application files.

| C# file | Source category |
| --- | --- |
| [CancellationLoggingChecks.cs](../NativeDeviceTests/CancellationLoggingChecks.cs) | Test/tool source |
| [CrashChecks.cs](../NativeDeviceTests/CrashChecks.cs) | Test/tool source |
| [EmptyPlaylistChecks.cs](../NativeDeviceTests/EmptyPlaylistChecks.cs) | Test/tool source |
| [Program.cs](../NativeDeviceTests/Program.cs) | Test/tool source |
| [RC8Checks.cs](../NativeDeviceTests/RC8Checks.cs) | Test/tool source |
| [RC9Checks.cs](../NativeDeviceTests/RC9Checks.cs) | Test/tool source |
| [ReadinessChecks.cs](../NativeDeviceTests/ReadinessChecks.cs) | Test/tool source |
| [SortOrderChecks.cs](../NativeDeviceTests/SortOrderChecks.cs) | Test/tool source |

## NativeOrchestrationTests

Project: [NativeOrchestrationTests.csproj](../NativeOrchestrationTests/NativeOrchestrationTests.csproj) — `net9.0-windows`. Offline native orchestration checks referencing the application.

| C# file | Source category |
| --- | --- |
| [Program.cs](../NativeOrchestrationTests/Program.cs) | Test/tool source |

## NativeProductionRunner

Project: [NativeProductionRunner.csproj](../NativeProductionRunner/NativeProductionRunner.csproj) — `net9.0-windows`. Manual/live-capable console utility. Not part of CI; do not run device operations as an offline check.

| C# file | Source category |
| --- | --- |
| [AutomatedDeleteWake.cs](../NativeProductionRunner/AutomatedDeleteWake.cs) | Test/tool source |
| [Program.cs](../NativeProductionRunner/Program.cs) | Test/tool source |

## Packaging/MsiBuilder

Project: [MsiBuilder.csproj](../Packaging/MsiBuilder/MsiBuilder.csproj) — `net9.0-windows`. Native Windows Installer authoring console tool; separate from the application.

| C# file | Source category |
| --- | --- |
| [Program.cs](../Packaging/MsiBuilder/Program.cs) | Test/tool source |

## RecoveryTests

Project: [RecoveryTests.csproj](../RecoveryTests/RecoveryTests.csproj) — `net9.0`. Offline journal/recovery checks; explicitly links application recovery files.

| C# file | Source category |
| --- | --- |
| [Program.cs](../RecoveryTests/Program.cs) | Test/tool source |

## RunHistoryDurabilityTests

Project: [RunHistoryDurabilityTests.csproj](../RunHistoryDurabilityTests/RunHistoryDurabilityTests.csproj) — `net9.0`. Offline run-history persistence checks; links history service and log model.

| C# file | Source category |
| --- | --- |
| [Program.cs](../RunHistoryDurabilityTests/Program.cs) | Test/tool source |

## Testing/InstalledWorkflowAcceptance

Project: [InstalledWorkflowAcceptance.csproj](../Testing/InstalledWorkflowAcceptance/InstalledWorkflowAcceptance.csproj) — `net9.0-windows`. Legacy installed/live acceptance harness. Its project still expects MediaMonkey Zidoo Sync.dll, whereas production uses MMZidooSync.dll. Requires adaptation and explicit scoped live authorization before reuse.

| C# file | Source category |
| --- | --- |
| [DeferredAcceptance.cs](../Testing/InstalledWorkflowAcceptance/DeferredAcceptance.cs) | Test/tool source |
| [NativeAcceptance.cs](../Testing/InstalledWorkflowAcceptance/NativeAcceptance.cs) | Test/tool source |
| [NavigationDiagnosis.cs](../Testing/InstalledWorkflowAcceptance/NavigationDiagnosis.cs) | Test/tool source |
| [OrderingDiagnosis.cs](../Testing/InstalledWorkflowAcceptance/OrderingDiagnosis.cs) | Test/tool source |
| [Program.cs](../Testing/InstalledWorkflowAcceptance/Program.cs) | Test/tool source |
| [RefreshDiagnosis.cs](../Testing/InstalledWorkflowAcceptance/RefreshDiagnosis.cs) | Test/tool source |

## Testing/MigrationTests

Project: [MigrationTests.csproj](../Testing/MigrationTests/MigrationTests.csproj) — `net9.0-windows`. Historical migration harness. Its standalone project has no links/references for application types used by Program.cs; requires its original preparation or adaptation. Not a production CI test.

| C# file | Source category |
| --- | --- |
| [Program.cs](../Testing/MigrationTests/Program.cs) | Test/tool source |

## Testing/NeutralSafeWorkflowTests

Project: [NeutralSafeWorkflowTests.csproj](../Testing/NeutralSafeWorkflowTests/NeutralSafeWorkflowTests.csproj) — `net9.0-windows`. Separate desktop workflow checks; references the application.

| C# file | Source category |
| --- | --- |
| [Program.cs](../Testing/NeutralSafeWorkflowTests/Program.cs) | Test/tool source |

## Testing/RepositoryPolicyChecks

Project: [RepositoryPolicyChecks.csproj](../Testing/RepositoryPolicyChecks/RepositoryPolicyChecks.csproj) — `net9.0`. Tracked-file publication policy checks.

| C# file | Source category |
| --- | --- |
| [Program.cs](../Testing/RepositoryPolicyChecks/Program.cs) | Test/tool source |

## WpfWorkflowTests

Project: [WpfWorkflowTests.csproj](../WpfWorkflowTests/WpfWorkflowTests.csproj) — `net9.0-windows`. Desktop UI/configuration/theme checks. Uses disposable configuration roots and references the application.

| C# file | Source category |
| --- | --- |
| [Program.cs](../WpfWorkflowTests/Program.cs) | Test/tool source |
| [RC9RepairUiChecks.cs](../WpfWorkflowTests/RC9RepairUiChecks.cs) | Commented-out deferred feature tests; not active repair UI |
| [RC9ServiceChecks.cs](../WpfWorkflowTests/RC9ServiceChecks.cs) | Test/tool source |
| [ReviewThemeChecks.cs](../WpfWorkflowTests/ReviewThemeChecks.cs) | Test/tool source |

## Supporting project files

Application XAML: [App.xaml](../MediaMonkeyZidooSync/App.xaml), [HelpWindow.xaml](../MediaMonkeyZidooSync/HelpWindow.xaml), [MainWindow.xaml](../MediaMonkeyZidooSync/MainWindow.xaml), [ZidooDiscoveryWindow.xaml](../MediaMonkeyZidooSync/ZidooDiscoveryWindow.xaml).

The application project specifies packaged configuration, artwork, notices and documentation. `global.json` pins the SDK. `.github/workflows/build.yml` lists the automated offline CI steps. The MSI revision is separate from the application semantic version. See [release 1.1](../RELEASE-1.1.md).

Update this index when adding or removing tracked C# files/projects. Historical alpha/RC notes remain historical records; the production release and current-status introduction identify the current supported state.
