using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MediaMonkeyZidooSync;
using MediaMonkeyZidooSync.Services;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++; Console.WriteLine("PASS: " + name);
    }
    private static void Pump(int milliseconds = 150)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static T Field<T>(object obj, string name) where T : class =>
        (T)(obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(obj) ?? throw new Exception("Missing " + name));
    private static void Invoke(object obj, string name) =>
        obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(obj, new object[] { obj, new RoutedEventArgs() });
    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "MMZS-WPF-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // This test has no access to the normal ProgramData configuration directory.
            // Match App.OnStartup: otherwise this test host can display a white window on a dark desktop.
#pragma warning disable WPF0001
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
            Check(app.ThemeMode == ThemeMode.System, "Deep-workflow test host uses system theme");
            Console.WriteLine($"Test host PID: {Environment.ProcessId}; ThemeMode: {app.ThemeMode}");
#pragma warning restore WPF0001
            var window = new MainWindow(root);
            window.Show(); Pump(400);
            var inspect = (Button)(typeof(MainWindow).GetMethod("FindInspectNativePlanButton", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null) ?? throw new Exception("Inspect button missing"));
            Check(!inspect.IsEnabled, "Inspect Plan initially disabled");
            var validate=Field<Button>(window,"ValidateMediaMonkeyButton");
            var verify=Field<Button>(window,"VerifyNativeExporterButton");
            var review=Field<Button>(window,"ReviewSynchronizationButton");
            var run=Field<Button>(window,"RunButton");
            Check(validate.IsEnabled && !verify.IsEnabled && !review.IsEnabled && !run.IsEnabled,"Only validation is enabled at startup");
            Check(typeof(MainWindow).GetField("RecoverSynchronizationButton",BindingFlags.Instance|BindingFlags.NonPublic) is null,"Unrequested recovery button removed");
            var flow=Field<SynchronizationWorkflow>(window,"_workflow");
            void Refresh()=>typeof(MainWindow).GetMethod("RefreshWorkflowControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            flow.BeginValidation();Refresh();
            Check(!validate.IsEnabled && !verify.IsEnabled && !run.IsEnabled,"Busy validation disables later steps");
            flow.EndValidation(true);Refresh();Check(verify.IsEnabled && !run.IsEnabled,"Validation enables verification only");
            flow.BeginVerification();flow.EndVerification(true);
            typeof(MainWindow).GetField("_preparedReview",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,Array.Empty<NativeProductionSyncService.ReviewedChange>());
            Refresh();Check(review.IsEnabled && !run.IsEnabled,"Verified exporter requires review before running");
            flow.AcceptReview();Refresh();Check(run.IsEnabled,"Accepted review enables synchronization");
            typeof(MainWindow).GetMethod("InvalidateWorkflow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            Check(!verify.IsEnabled && !review.IsEnabled && !run.IsEnabled,"Configuration invalidation resets enabled buttons");
            var entries = new NativePlanInspectionItem[] {
                new("WPF-Test-Only.m3u", PlaylistTransactionAction.Create, "Synthetic read-only UI fixture", false)
            };
            typeof(MainWindow).GetField("_lastNativePlanInspection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, entries);
            inspect.IsEnabled = true;
            Invoke(window, "InspectNativePlan_Click"); Pump();
            var first = Field<Window>(window, "_nativePlanWindow");
            Check(first.IsVisible, "Proposed Changes opens");
            Invoke(window, "InspectNativePlan_Click"); Pump();
            Check(ReferenceEquals(first, Field<Window>(window, "_nativePlanWindow")), "Reopen while visible reuses window");
            Check(first.Owner is null, "Proposed Changes modeless and unowned for normal stacking");
            window.Activate(); Pump();
            Check(window.IsVisible && first.IsVisible, "Main and Proposed Changes coexist");
            first.Activate(); Pump();
            Check(first.IsVisible, "Proposed Changes can reactivate");
            first.Close(); Pump();
            Check(typeof(MainWindow).GetField("_nativePlanWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is null,
                "Closing clears inspector reference");
            Invoke(window, "InspectNativePlan_Click"); Pump();
            var second = Field<Window>(window, "_nativePlanWindow");
            Check(second.IsVisible && !ReferenceEquals(first, second), "Closed Proposed Changes reopens");
            second.Close();
            var history=Field<DataGrid>(window,"RunHistoryGrid");
            var tabs=Field<TabControl>(window,"MainTabs");
            var historyTab=tabs.Items.OfType<TabItem>().Single(tab=>Equals(tab.Header,"Run History / Logs"));
            tabs.SelectedItem=historyTab;
            string longLog=@"E:\Temp\MediaMonkey\RunHistory\"+new string('a',220)+".log";
            var historyRecord=new RunHistoryService.RunRecord("text-selection-test",DateTime.Now,"Synchronization","Cancelled","00:01:00",longLog);
            history.ItemsSource=new[]{historyRecord};window.Width=1050;window.UpdateLayout();Pump();
            history.ScrollIntoView(historyRecord,history.Columns.Last());history.UpdateLayout();Pump();
            System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject parent)
            {
                for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++)
                {var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
            }
            var scroller=Descendants(history).OfType<ScrollViewer>().First();
            Check(scroller.ScrollableWidth>0 && scroller.ComputedHorizontalScrollBarVisibility==Visibility.Visible,"Long Run History paths expose a horizontal scrollbar");
            scroller.ScrollToRightEnd();Pump();Check(scroller.HorizontalOffset>0,"Run History can scroll horizontally to long log paths");
            var pathBox=Descendants(history).OfType<TextBox>().Single(box=>box.Text==longLog);
            history.UnselectAll();history.CurrentCell=new DataGridCellInfo(historyRecord,history.Columns.Last());pathBox.Focus();pathBox.Select(3,4);Pump();
            Check(pathBox.SelectedText=="Temp" && history.SelectedItems.Count==0,"Selecting part of a log path does not select the whole row");
            Check(pathBox.IsReadOnly && Equals(history.CurrentCell.Item,historyRecord),"Selected log cell remains read-only and identifies its run");
            Check(history.SelectionUnit==DataGridSelectionUnit.CellOrRowHeader && history.RowHeaderWidth>0,"Whole-row selection requires explicit row-header selection");
            pathBox.SelectAll();Check(pathBox.SelectedText==longLog,"Select All inside a log path selects only that field");
            Check(Field<DataGrid>(window,"ConfigHistoryGrid").SelectionUnit==DataGridSelectionUnit.FullRow,"Configuration Archive selection remains unchanged");
            tabs.SelectedIndex=0;Pump();
            window.Close(); Pump();
            app.Shutdown();

            var svc = new ConfigurationService(root);
            var seed = Path.Combine(AppContext.BaseDirectory, "Resources", "MediaMonkey-Zidoo-Config-v1.0.0.json");
            if (!File.Exists(seed)) throw new Exception("Packaged seed missing: " + seed);
            var config = svc.LoadAsync(seed).GetAwaiter().GetResult();
            Check(string.IsNullOrEmpty(config.Zidoo.IPAddress) && string.IsNullOrEmpty(config.NAS.Host) && string.IsNullOrEmpty(config.MediaMonkey.DatabasePath), "Neutral seed has no live endpoints");
            bool blankRejected = false;
            try { svc.SaveAsync(config).GetAwaiter().GetResult(); } catch (InvalidDataException) { blankRejected = true; }
            Check(blankRejected, "Incomplete neutral configuration cannot be saved as valid");
            // Offline fixture for persistence/archive tests; never used for a device call.
            config.Application.ReportRoot = Path.Combine(root, "Reports");
            config.Zidoo.IPAddress = "192.0.2.1"; config.Zidoo.MacAddress = "001122334455";
            config.Zidoo.BroadcastAddress = "192.0.2.255"; config.Zidoo.ExpectedModel = "TEST";
            config.NAS.Host = "offline.invalid"; config.NAS.ShareName = "test";
            config.NAS.MusicFolderName = "music"; config.NAS.PlaylistFolderName = "lists";
            config.NAS.MusicRootUNC = @"\\offline.invalid\test\music";
            config.NAS.PlaylistFolderUNC = @"\\offline.invalid\test\lists";
            config.MediaMonkey.DatabasePath = Path.Combine(root, "nonexistent.db"); config.MediaMonkey.PlaylistParent = "TEST";
            svc.SaveAsync(config).GetAwaiter().GetResult();
            var original = config.Zidoo.PlaylistGridColumns;
            config.Zidoo.PlaylistGridColumns = original + 1;
            svc.SaveAsync(config).GetAwaiter().GetResult();
            var reopened = new ConfigurationService(root).LoadAsync(seed).GetAwaiter().GetResult();
            Check(reopened.Zidoo.PlaylistGridColumns == original + 1, "Configuration survives service restart");
            Check(svc.GetArchiveHistory().Count >= 1, "Saving creates configuration archive");
            reopened.Zidoo.PlaylistGridColumns = 0;
            bool rejected = false;
            try { svc.SaveAsync(reopened).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid configuration rejected");
            Check(new ConfigurationService(root).LoadAsync(seed).GetAwaiter().GetResult().Zidoo.PlaylistGridColumns == original + 1,
                "Rejected configuration does not replace persisted values");
            svc.RestoreArchive(svc.GetArchiveHistory().First());
            Check(svc.LoadAsync(seed).GetAwaiter().GetResult().Zidoo.PlaylistGridColumns == original,
                "Archived configuration can be restored");
            Console.WriteLine($"RESULT: {passed} / {passed} PASS. Isolated UI and configuration tests; no live sync.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { try { Directory.Delete(root, true); } catch { Console.Error.WriteLine("WARNING: test sandbox cleanup failed: " + root); } }
    }
}
