// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.0 (Major/Minor controlled by the product owner)
// Development channel: alpha.6
// Purpose: Application startup and native Windows Fluent system-theme activation.
// Remarks:
// - Build and revision are compiler-generated through AssemblyVersion 1.0.*.
// - Released artifacts are immutable; any code change requires a new build identity.
// - Production PowerShell synchronization mechanics are not changed by this source-governance pass.
// ============================================================================
using System.Windows;
using System;
using System.Threading.Tasks;
using MediaMonkeyZidooSync.Services;

namespace MediaMonkeyZidooSync;

/// <summary>
/// WPF application entry point. Delegates application appearance to the native
/// WPF system theme so the UI follows the Windows light/dark and Fluent presentation.
/// </summary>
public partial class App : Application
{
#pragma warning disable WPF0001
    /// <summary>
    /// Applies the native system theme before the startup window is created.
    /// </summary>
    /// <param name="e">Application startup arguments supplied by WPF.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            DiagnosticReportingService.Shared.Report("WPF dispatcher", args.Exception, "Critical");
            // Do not mark handled: continuing after an unknown UI exception may be unsafe.
            try { MessageBox.Show("An unexpected application error occurred. Details were recorded in the diagnostic log.",
                "MediaMonkey Zidoo Sync — Application Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch { /* The dispatcher may be unable to display UI. */ }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                DiagnosticReportingService.Shared.Report("AppDomain unhandled", ex, "Critical");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticReportingService.Shared.Report("Unobserved task", args.Exception, "Critical");
            // Runtime policy decides whether process termination is necessary.
        };
        // Resolve isolated mode before WPF constructs MainWindow.
        // An invalid marker/path must fail before normal configuration can be opened.
        IsolatedStartupMode.ResolveRoot();
        ThemeMode = ThemeMode.System;
        base.OnStartup(e);
    }
#pragma warning restore WPF0001
}
