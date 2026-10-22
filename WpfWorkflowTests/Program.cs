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
    private static void Invoke(object obj, string name) { var prior=SynchronizationContext.Current; try { SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(obj, new object[] { obj, new RoutedEventArgs() }); } finally { SynchronizationContext.SetSynchronizationContext(prior); } }
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
            string? openedLog = null;
            var window = new MainWindow(root, path => openedLog = path);
            window.Show(); Pump(400);
            Check(window.Title=="MM Zidoo Sync" && typeof(MainWindow).Assembly.GetName().Name=="MMZidooSync" && ApplicationVersionProvider.ProductVersion=="1.1.0-rc.10", "RC product name, assembly and version match approved identity");
            Check(typeof(MainWindow).GetMethod("RunNativeAssessment_Click", BindingFlags.Instance | BindingFlags.NonPublic) is null && typeof(MainWindow).GetMethod("InspectNativePlan_Click", BindingFlags.Instance | BindingFlags.NonPublic) is null, "Optional assessment handlers are excluded from compilation");
            Check(window.FindName("RunNativeAssessmentButton") is null && window.FindName("InspectNativePlanButton") is null, "Optional diagnostics controls are absent");
            Check(window.Icon is not null, "Main window loads application artwork");
            var validate=Field<Button>(window,"ValidateMediaMonkeyButton");
            var verify=Field<Button>(window,"VerifyNativeExporterButton");
            var review=Field<Button>(window,"ReviewSynchronizationButton");
            var run=Field<Button>(window,"RunButton");
            Check(!validate.IsEnabled && !verify.IsEnabled && !review.IsEnabled && !run.IsEnabled,"No configuration disables all synchronization steps");
            Check(!Field<TabItem>(window,"SynchronizationTab").IsEnabled && ReferenceEquals(Field<TabControl>(window,"MainTabs").SelectedItem,Field<TabItem>(window,"ConfigurationTab")),"First startup selects Configuration and blocks Synchronization");
            Check(Field<TabItem>(window,"RunHistoryTab").IsEnabled,"Incomplete setup keeps Run History available");
            var draftRoot=Path.Combine(root,"DraftSetup");
            var draftWindow=new MainWindow(draftRoot,path=>openedLog=path);draftWindow.Show();Pump(300);
            Field<TextBox>(draftWindow,"ZidooIp").Text="192.0.2.42";
            Invoke(draftWindow,"SaveConfig_Click");Pump(400);
            var draftService=new ConfigurationService(draftRoot);
            var savedDraft=draftService.LoadAsync(Path.Combine(AppContext.BaseDirectory,"Resources","MediaMonkey-Zidoo-Config-v1.0.0.json")).GetAwaiter().GetResult();
            Check(savedDraft.Zidoo.IPAddress=="192.0.2.42" && string.IsNullOrEmpty(savedDraft.MediaMonkey.PlaylistParent),"Partial setup saves and reloads without all required fields");
            Check(!Field<Button>(draftWindow,"ValidateMediaMonkeyButton").IsEnabled && Field<TabItem>(draftWindow,"RunHistoryTab").IsEnabled && Field<TextBlock>(draftWindow,"ConfigMessage").Text.Contains("Setup progress saved"),"Saved draft keeps synchronization blocked and history accessible");
            var draftLog=Field<string>(draftWindow,"_configurationLogPath");
            Check(File.Exists(draftLog) && new RunHistoryService().List(Path.Combine(draftRoot,"Reports")).Any(x=>x.Result=="Draft saved"),"Partial save creates an openable setup history log");
            Check(draftWindow.FindName("OpenConfigurationLogButton") is null,"Setup log button removed; history provides log access");
            Field<TextBox>(draftWindow,"ApiPort").Text="bad";Invoke(draftWindow,"SaveConfig_Click");Pump(300);
            var failedSetupLog=Field<string>(draftWindow,"_configurationLogPath");
            Check(Field<TextBlock>(draftWindow,"ConfigMessage").Text.Contains("Zidoo API Port") && File.ReadAllText(failedSetupLog).Contains("InvalidDataException"),"Invalid numeric input names the field and records diagnostic details");
            Check(new ConfigurationService(draftRoot).LoadAsync(Path.Combine(AppContext.BaseDirectory,"Resources","MediaMonkey-Zidoo-Config-v1.0.0.json")).GetAwaiter().GetResult().Zidoo.ApiPort==savedDraft.Zidoo.ApiPort,"Failed save preserves the previous persisted draft");
            Field<TextBox>(draftWindow,"ApiPort").Text=savedDraft.Zidoo.ApiPort.ToString();
            draftWindow.Close();Pump();
            var draftReopened=new MainWindow(draftRoot);draftReopened.Show();Pump(300);
            Check(Field<TextBox>(draftReopened,"ZidooIp").Text=="192.0.2.42" && Field<TabControl>(draftReopened,"MainTabs").SelectedIndex==1 && Field<TabItem>(draftReopened,"RunHistoryTab").IsEnabled,"Restart restores the draft and keeps Configuration and Logs available");
            draftReopened.Close();Pump();
            typeof(MainWindow).GetField("_configurationReady",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);
            typeof(MainWindow).GetMethod("RefreshWorkflowControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            Check(validate.IsEnabled,"Saved valid configuration enables validation");
            Field<TabControl>(window,"MainTabs").SelectedItem=Field<TabItem>(window,"SynchronizationTab");
            var output=Field<RichTextBox>(window,"OutputRichText");var outputEvents=Field<System.Collections.Generic.List<MediaMonkeyZidooSync.Models.LogEvent>>(window,"_events");
            for(int index=0;index<200;index++)outputEvents.Add(new(DateTime.Now,MediaMonkeyZidooSync.Models.LogLevel.Blue,"Scroll position fixture line "+index));
            void RedrawOutput()=>typeof(MainWindow).GetMethod("RefreshOutput",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            RedrawOutput();Pump(300);output.ScrollToVerticalOffset(50);Pump();var readingOffset=output.VerticalOffset;RedrawOutput();Pump();
            Check(readingOffset>0 && Math.Abs(output.VerticalOffset-readingOffset)<2,"Output updates preserve the reader's scrolled position");
            var oldParagraphs=output.Document.Blocks.Count;
            output.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
            outputEvents.Add(new(DateTime.Now,MediaMonkeyZidooSync.Models.LogLevel.Blue,"Event during scrollbar drag"));RedrawOutput();
            Check(output.Document.Blocks.Count==oldParagraphs,"Output does not redraw while the scrollbar thumb is being dragged");
            output.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});Pump();
            Check(output.Document.Blocks.Count==oldParagraphs+1 && Math.Abs(output.VerticalOffset-readingOffset)<2,"Ending scrollbar dragging displays new events without jumping to the bottom");
            outputEvents.Clear();RedrawOutput();

            Check(ZidooDiscoveryService.BroadcastForSubnet("192.168.1.160","192.168.1.20","255.255.255.0")=="192.168.1.255","Discovery derives the matching subnet broadcast");
            Check(ZidooDiscoveryService.BroadcastForSubnet("10.0.3.50","10.0.2.1","255.255.254.0")=="10.0.3.255","Broadcast calculation respects non-24-bit masks");
            Check(ZidooDiscoveryService.BroadcastForSubnet("192.168.2.20","192.168.1.20","255.255.255.0") is null,"Discovery does not guess a broadcast for an unrelated subnet");
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
// Deferred repair: RC9ServiceChecks.Run(root,Check);
// Deferred repair: RC9RepairUiChecks.Run(window,Check);
            ReviewThemeChecks.Run(window,Check);
            var splashType=typeof(MainWindow).Assembly.GetType("MediaMonkeyZidooSync.StartupSplash")!;
            var splashProbe=new Window { Width=200,Height=100,ShowInTaskbar=false,Content=new TextBlock{Text="Splash timing fixture"} };
            var splashObject=Activator.CreateInstance(splashType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{splashProbe},null)!;
            bool SplashVisible()=> (bool)splashType.GetProperty("IsVisible",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(splashObject)!;
            var renderedAt=new System.Diagnostics.Stopwatch();splashProbe.ContentRendered+=(_,_)=>renderedAt.Start();
            int heartbeat=0;var beat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};beat.Tick+=(_,_)=>heartbeat++;beat.Start();
            splashProbe.Show();Pump(350);Check(SplashVisible(),"Splash remains visible after main UI first renders");
            Pump(1000);Check(SplashVisible(),"Splash does not close before its two-second interval");
            Pump(1000);Console.WriteLine($"Splash diagnostic: visible={SplashVisible()}, renderedSeconds={renderedAt.Elapsed.TotalSeconds:F3}, heartbeat={heartbeat}");Check(!SplashVisible() && renderedAt.Elapsed.TotalSeconds>=2 && renderedAt.Elapsed.TotalSeconds<4,"Splash closes automatically two seconds after rendering without activation");
            Check(heartbeat>25,"UI dispatcher remains responsive while the splash timer runs");beat.Stop();splashProbe.Close();
            var trayWindow=new Window { Width=200, Height=100, ShowInTaskbar=false };
            var branding=typeof(MainWindow).Assembly.GetType("MediaMonkeyZidooSync.ApplicationBranding")!;
            var trayState=(Func<bool>)branding.GetMethod("AttachTray",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{trayWindow})!;
            trayWindow.Show();Pump();Check(trayState(),"Tray icon registers with Windows using supplied artwork");
            trayWindow.Close();Pump();Check(!trayState(),"Closing the window removes its tray icon");
            var about=new AboutWindow{Owner=window};about.Show();Pump();
            Check(((Image)about.FindName("AboutBrandImage")).Source is not null && about.Icon is not null,"About displays supplied artwork and application icon");
            Check(Descendants(about).OfType<TextBlock>().Any(t=>t.Text.Contains("MediaMonkey → Zidoo playlist synchronization")),"About shows approved synchronization description");
            about.Close();
            var help = new HelpWindow("GettingStarted");help.Show();Pump();
            Check(((Image)help.FindName("HelpBrandImage")).Source is not null && help.Icon is not null, "Help loads supplied artwork and application icon");
            var helpDocument=((FlowDocumentScrollViewer)help.FindName("TopicBody")).Document;
            Check(new System.Windows.Documents.TextRange(helpDocument.ContentStart,helpDocument.ContentEnd).Text.Contains("MediaMonkey → Zidoo playlist synchronization"),"Help shows approved synchronization description");
            var preview = new System.Windows.Media.Imaging.RenderTargetBitmap((int)help.ActualWidth,(int)help.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            preview.Render(help);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
            using(var png=File.Create(Path.Combine(root,"help-preview.png")))encoder.Save(png);
            var artifacts=Environment.GetEnvironmentVariable("MMZS_UI_ARTIFACT_DIRECTORY");
            if(!string.IsNullOrWhiteSpace(artifacts)){Directory.CreateDirectory(artifacts);File.Copy(Path.Combine(root,"help-preview.png"),Path.Combine(artifacts,"help-preview.png"),true);
                var mainPreview=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);mainPreview.Render(window);var mainEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder();mainEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(mainPreview));using(var png=File.Create(Path.Combine(artifacts,"main-preview.png")))mainEncoder.Save(png);}
            help.SelectTopic("Configuration");Pump();
            var configHelp=new System.Windows.Documents.TextRange(helpDocument.ContentStart,helpDocument.ContentEnd).Text;
            Check(configHelp.Contains(@"\\MusicServer\Media\Music") && configHelp.Contains("Host = MusicServer") && configHelp.Contains("exact name, including capitalization") && configHelp.Contains("Save your progress"),"Configuration Help explains storage names, playlist parent and partial saves");
            Check(!System.Windows.Markup.XamlWriter.Save(help).Contains("apidoc.zidoo.tv"),"End-user Help omits the unlocalized developer API link");
            Check(helpDocument.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().Any(block=>block.Child is Image image&&image.Source is not null) && helpDocument.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().Any(block=>block.Child is StackPanel),"Configuration Help contains the MediaMonkey picture and folder diagram");
            if(!string.IsNullOrWhiteSpace(artifacts)){
                var configPreview=new System.Windows.Media.Imaging.RenderTargetBitmap((int)help.ActualWidth,(int)help.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);configPreview.Render(help);var configEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder();configEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(configPreview));using(var png=File.Create(Path.Combine(artifacts,"configuration-help-preview.png")))configEncoder.Save(png);
            }
            Check(configHelp.Contains("Load Configuration") && !configHelp.Contains("choose A Zidoo, or"),"Configuration Help explains loading settings and generic playlist storage");
            help.Close();            var history=Field<DataGrid>(window,"RunHistoryGrid");
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
            var actualLog=Path.Combine(root,"specific-run.log");File.WriteAllText(actualLog,"offline test log");
            var actualRecord=historyRecord with { Id="specific-run", LogPath=actualLog };
            history.ItemsSource=new[]{actualRecord};history.UpdateLayout();Pump();
            history.ScrollIntoView(actualRecord,history.Columns.Last());history.UpdateLayout();Pump();
            var actualBox=Descendants(history).OfType<TextBox>().Single(box=>box.Text==actualLog);
            history.UnselectAll();history.CurrentCell=new DataGridCellInfo();
            actualBox.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left)
                {RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent});
            Check(Field<Button>(window,"OpenSelectedLogButton").IsEnabled && history.SelectedItems.Count==0,"Clicking selectable log text identifies a run without selecting its row");
            var highlightedCell=Descendants(history).OfType<DataGridCell>().Single(cell=>cell.Content is ContentPresenter presenter && Descendants(presenter).OfType<TextBox>().Any(box=>ReferenceEquals(box,actualBox)));
            Check(highlightedCell.IsSelected && Equals(highlightedCell.Background,SystemColors.HighlightBrush),"Clicked log cell has a visible system selection highlight");
            Field<Button>(window,"OpenSelectedLogButton").Focus();Pump();
            Check(highlightedCell.IsSelected && Equals(highlightedCell.Background,SystemColors.HighlightBrush),"Log highlight persists when focus moves to Open Selected Log");
            Invoke(window,"OpenRunLog_Click");
            Check(openedLog==actualLog,"Open Selected Log opens the clicked cell's exact log after focus moves to its button");
            flow.BeginValidation();flow.EndValidation(true);flow.BeginVerification();flow.EndVerification(true,false);Refresh();
            Check(!review.IsEnabled && !run.IsEnabled,"No changes disables both Review Changes and Run Synchronization");
            var recoveryConfig=new MediaMonkeyZidooSync.Models.AppConfig();recoveryConfig.NAS.PlaylistFolderUNC=root;
            typeof(MainWindow).GetField("_config",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,recoveryConfig);
            var recoveryProbe=typeof(MainWindow).GetMethod("HasPendingRecovery",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Check(!(bool)recoveryProbe.Invoke(window,null)!,"Clean matching playlists do not imply pending recovery");
            var marker=Path.Combine(root,".mmzs-production-pending.json");File.WriteAllText(marker,"retained recovery fixture");
            bool needsRecovery=(bool)recoveryProbe.Invoke(window,null)!;
            typeof(MainWindow).GetField("_preparedReview",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,Array.Empty<NativeProductionSyncService.ReviewedChange>());
            flow.BeginVerification();flow.EndVerification(true,needsRecovery);Refresh();
            Check(needsRecovery && review.IsEnabled && !run.IsEnabled,"Retained transaction keeps review available but cannot bypass review to run");
            File.Delete(marker);
            tabs.SelectedIndex=2;history.Columns[1].Width=333;history.Columns[1].DisplayIndex=0;Pump();
            window.Close(); Pump();
            var savedUi=new UiStateService(Path.Combine(root,"ui-state.json")).LoadAsync().GetAwaiter().GetResult();
            Check(savedUi.SelectedTabIndex==2 && savedUi.GridColumns["RunHistoryGrid"].Any(c=>c.Header=="Operation" && c.Width==333),"Layout is saved in the isolated profile, including tab and grid columns");
            var reopenedWindow=new MainWindow(root);reopenedWindow.Show();Pump(500);
            var reopenedTabs=Field<TabControl>(reopenedWindow,"MainTabs");var reopenedGrid=Field<DataGrid>(reopenedWindow,"RunHistoryGrid");
            Console.WriteLine($"Restored layout: tab={reopenedTabs.SelectedIndex}, width={reopenedGrid.Columns[1].Width.Value}, order={reopenedGrid.Columns[1].DisplayIndex}");
            Check(reopenedTabs.SelectedIndex==1 && reopenedGrid.Columns[1].Width.Value==333 && reopenedGrid.Columns[1].DisplayIndex==0,"Grid layout restores while incomplete configuration takes precedence over the saved tab");
            reopenedWindow.Close();Pump();
            foreach(var outcome in new[]{"empty","found","failed"})
            {
                var discoveryRoot=Path.Combine(root,"Discovery-"+outcome);
                Func<Task<IReadOnlyList<ZidooDiscoveryResult>>> response=()=>outcome=="failed"
                    ? Task.FromException<IReadOnlyList<ZidooDiscoveryResult>>(new IOException("offline discovery fixture"))
                    : Task.FromResult<IReadOnlyList<ZidooDiscoveryResult>>(outcome=="found"
                        ? new[]{new ZidooDiscoveryResult("192.0.2.10","TEST","offline","00:11:22:33:44:55","","test",true)}
                        : Array.Empty<ZidooDiscoveryResult>());
                var discoveryWindow=new MainWindow(discoveryRoot,discoverZidoo:response);discoveryWindow.Show();Pump(300);
                Invoke(discoveryWindow,"DiscoverZidoo_Click");Pump(300);
                var entries=new RunHistoryService().List(Path.Combine(discoveryRoot,"Reports"));
                Check(entries.Count==1 && entries[0].Operation=="Zidoo Discovery" && entries[0].Result==(outcome=="empty"?"No device found":outcome=="found"?"Device found":"Failed"),"Discovery records the "+outcome+" outcome before configuration exists");
                Check(File.Exists(entries[0].LogPath) && File.ReadAllText(entries[0].LogPath).Contains("no wake or playlist changes"),"Discovery "+outcome+" has an openable independent audit log");
                var model=Field<MediaMonkeyZidooSync.Models.AppConfig>(discoveryWindow,"_config");model.Application.ReportRoot=Path.Combine(discoveryRoot,"NewReports");
                Invoke(discoveryWindow,"RefreshRunHistory_Click");
                Check(Field<DataGrid>(discoveryWindow,"RunHistoryGrid").Items.Count==1,"Discovery history remains visible after changing the report folder: "+outcome);
                discoveryWindow.Close();Pump();
            }
            var byteFixture=Path.Combine(root,"frozen-bytes.m3u");var frozenLines=new[]{"#EXTM3U","../Artist/Track.flac"};
            File.WriteAllLines(byteFixture,frozenLines,new System.Text.UTF8Encoding(true));
            Check(File.ReadAllBytes(byteFixture).SequenceEqual(FrozenM3uContent.Bytes(frozenLines)),"Native candidate bytes match the frozen UTF-8 BOM Windows WriteAllLines output");
            File.WriteAllText(byteFixture,string.Join("\n",frozenLines)+"\n",new System.Text.UTF8Encoding(false));
            Check(File.ReadAllLines(byteFixture).SequenceEqual(frozenLines) && !File.ReadAllBytes(byteFixture).SequenceEqual(FrozenM3uContent.Bytes(frozenLines)),"Matching text with different BOM or line endings remains a byte-level change under the frozen process");
            var scope=Path.Combine(root,"FrozenFileScope");Directory.CreateDirectory(scope);
            foreach(var name in new[]{"visible.m3u","hidden.m3u","system.m3u","ignore.txt"})File.WriteAllText(Path.Combine(scope,name),"fixture");
            File.SetAttributes(Path.Combine(scope,"hidden.m3u"),FileAttributes.Hidden);
            File.SetAttributes(Path.Combine(scope,"system.m3u"),FileAttributes.System);
            Directory.CreateDirectory(Path.Combine(scope,"Child"));File.WriteAllText(Path.Combine(scope,"Child","nested.m3u"),"fixture");
            Check(FrozenPlaylistFiles.Enumerate(scope).Select(Path.GetFileName).SequenceEqual(new[]{"visible.m3u"}),"Native M3U enumeration matches frozen nonrecursive Get-ChildItem without Force: hidden/system files stay outside the baseline");
            var location=StorageSetupService.ParseMusic(@"\\Server\Share\Music");
            Check(location.Host=="Server" && location.Share=="Share" && location.Music=="Music","Network folder selection derives host, share and music name");
            Check(StorageSetupService.PlaylistName(@"\\Server\Share\Music",@"\\Server\Share\Music\A Zidoo")=="A Zidoo","Playlist selection derives child folder name");
            Check(StorageSetupService.PlaylistName(@"\\Server\Share\Music",@"\\Server\Share\Music")=="","Folder chooser permits equal roots without inventing a navigation name");
            Check(StorageSetupService.PlaylistName(@"\\Server\Share\Music",@"\\Server\OtherShare\Lists")=="","Folder chooser permits independent playlist locations without replacing manual navigation settings");
            Check(StorageSetupService.ParseMusic(@"\\Server\Share\Music\Archive").Path==@"\\Server\Share\Music\Archive","Music browser preserves nested paths instead of rejecting their layout");
            bool localRejected=false;try{StorageSetupService.ParseMusic(@"C:\Music");}catch(InvalidDataException){localRejected=true;}
            Check(localRejected,"Network setup refuses local-only music paths");
            var importFile=Path.Combine(root,"saved-import.json");File.WriteAllText(importFile,"{\"MediaMonkey\":{\"PlaylistParent\":\"My playlists\"}}");
            var imported=ConfigurationService.ReadImportAsync(importFile).GetAwaiter().GetResult();
            Check(imported.MediaMonkey.PlaylistParent=="My playlists" && imported.Zidoo is not null,"Configuration import supports incomplete saved JSON");
            var importHash=File.ReadAllText(importFile);
            var importWindow=new MainWindow(Path.Combine(root,"ImportUI"));importWindow.Show();Pump(300);
            typeof(MainWindow).GetMethod("ApplyImportedConfiguration",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(importWindow,new object[]{imported});
            Check(Field<TextBox>(importWindow,"PlaylistParent").Text=="My playlists" && !Field<TabItem>(importWindow,"SynchronizationTab").IsEnabled,"Imported values populate the form and require saving before synchronization");
            Check(File.ReadAllText(importFile)==importHash,"Loading a configuration preserves the source file");
            Invoke(importWindow,"HelpGuide_Click");Pump();
            var childHelp=Field<HelpWindow>(importWindow,"_helpWindow");
            Field<Expander>(importWindow,"RunHistorySection").IsExpanded=false;Field<Expander>(importWindow,"RecoverySection").IsExpanded=true;
            importWindow.Close();Pump();Check(!childHelp.IsVisible,"Closing the main window also closes Help");
            var restoredHistory=new MainWindow(Path.Combine(root,"ImportUI"));restoredHistory.Show();Pump(300);
            Check(!Field<Expander>(restoredHistory,"RunHistorySection").IsExpanded && Field<Expander>(restoredHistory,"RecoverySection").IsExpanded,"History and recovery section expansion survive restart");restoredHistory.Close();Pump();
            File.WriteAllText(importFile,"{\"LogPath\":\"C:\\\\outside.log\"}");bool wrongJson=false;try{ConfigurationService.ReadImportAsync(importFile).GetAwaiter().GetResult();}catch(InvalidDataException){wrongJson=true;}
            Check(wrongJson,"Configuration import rejects history JSON");
            File.WriteAllText(importFile,"{\"NAS\":null}");bool nullJson=false;try{ConfigurationService.ReadImportAsync(importFile).GetAwaiter().GetResult();}catch(InvalidDataException){nullJson=true;}
            Check(nullJson,"Configuration import rejects null configuration sections");
            var historyRoot=Path.Combine(root,"Retention");var histories=new RunHistoryService();
            var recoverySentinel=Path.Combine(historyRoot,"pending-recovery.json");Directory.CreateDirectory(historyRoot);File.WriteAllText(recoverySentinel,"retain recovery");
            for(int index=0;index<4;index++)histories.Save(historyRoot,DateTime.Now.AddMinutes(index),"offline","Complete",TimeSpan.Zero,Array.Empty<MediaMonkeyZidooSync.Models.LogEvent>());
            histories.Prune(historyRoot,2);var retained=histories.List(historyRoot);
            Check(retained.Count==2 && Directory.GetFiles(Path.Combine(historyRoot,"RunHistory"),"*.log").Length==2,"Retention removes old indexed log/index pairs");
            histories.Prune(historyRoot,0);Check(histories.List(historyRoot).Count==2,"Retention zero keeps all history");
            var outside=Path.Combine(root,"outside.log");File.WriteAllText(outside,"keep");bool unsafeDelete=false;try{histories.Delete(historyRoot,retained[0] with{LogPath=outside});}catch(InvalidDataException){unsafeDelete=true;}
            Check(unsafeDelete && File.ReadAllText(outside)=="keep","History cleanup refuses paths outside its own history folder");
            histories.Delete(historyRoot,retained[0]);Check(histories.List(historyRoot).Count==1 && File.ReadAllText(recoverySentinel)=="retain recovery","Selected history deletion preserves recovery data");
            var parentDb=Path.Combine(root,"parent-picker.db");
            using(var sql=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+parentDb+";Pooling=False")){sql.Open();using var command=sql.CreateCommand();command.CommandText="CREATE TABLE Playlists(IDPlaylist INTEGER,PlaylistName TEXT,ParentPlaylist INTEGER);INSERT INTO Playlists VALUES(1,'Imported Playlists',0),(2,'Jazz',1),(3,'Rock',1),(4,'Deep Rock',3),(5,'Other',0)";command.ExecuteNonQuery();}
            var dbBytes=File.ReadAllBytes(parentDb);var parents=PlaylistParentService.ListAsync(parentDb).GetAwaiter().GetResult();
            Check(parents.Single(x=>x.Name=="Imported Playlists").Descendants==3 && parents.Single(x=>x.Name=="Rock").Descendants==1,"Parent picker counts recursive child playlists and shows groups");
            Check(dbBytes.SequenceEqual(File.ReadAllBytes(parentDb)),"Parent picker leaves MediaMonkey database unchanged");
            using(var sql=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+parentDb+";Pooling=False")){sql.Open();using var command=sql.CreateCommand();command.CommandText="CREATE TABLE PlaylistSongs(IDPlaylist INTEGER,IDPlaylistSong INTEGER,IDSong INTEGER,SongOrder INTEGER);CREATE TABLE Songs(ID INTEGER,SongPath TEXT,Artist TEXT,SongTitle TEXT,SongLength INTEGER)";command.ExecuteNonQuery();}
            var planFolder=Path.Combine(root,"FrozenPlan");Directory.CreateDirectory(planFolder);
            var planConfig=new MediaMonkeyZidooSync.Models.AppConfig();planConfig.MediaMonkey.DatabasePath=parentDb;planConfig.MediaMonkey.PlaylistParent="Imported Playlists";planConfig.NAS.MusicRootUNC=root;planConfig.NAS.PlaylistFolderUNC=planFolder;planConfig.Application.ReportRoot=Path.Combine(root,"FrozenPlanReports");
            var jazzPath=Path.Combine(planFolder,"Jazz.m3u");File.WriteAllText(jazzPath,"#EXTM3U\n#EXTENC: UTF-8\n#PLAYLIST:Jazz\n",new System.Text.UTF8Encoding(false));
            var bytePlan=new MediaMonkeyExportTransactionPlanner().PrepareAsync(planConfig).GetAwaiter().GetResult();
            Check(bytePlan.Items.Single(item=>item.FileName=="Jazz.m3u").Action==PlaylistTransactionAction.Update,"Actual native planning classifies identical text with different frozen bytes as UPDATE");
            File.WriteAllBytes(jazzPath,FrozenM3uContent.Bytes(new[]{"#EXTM3U","#EXTENC: UTF-8","#PLAYLIST:Jazz"}));
            var samePlan=new MediaMonkeyExportTransactionPlanner().PrepareAsync(planConfig).GetAwaiter().GetResult();
            Check(samePlan.Items.Single(item=>item.FileName=="Jazz.m3u").Action==PlaylistTransactionAction.Unchanged,"Actual native planning leaves exact frozen-byte matches unchanged");
            File.SetAttributes(jazzPath,FileAttributes.Hidden);
            var hiddenPlan=new MediaMonkeyExportTransactionPlanner().PrepareAsync(planConfig).GetAwaiter().GetResult();
            Check(hiddenPlan.Items.Single(item=>item.FileName=="Jazz.m3u").Action==PlaylistTransactionAction.Create,"Actual native planning excludes hidden M3Us from the frozen baseline");
            var backupMethod=typeof(NativeProductionSyncService).GetMethod("RecoverySources",BindingFlags.Static|BindingFlags.NonPublic)!;
            var physicalHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(jazzPath)));
            var visible=FrozenPlaylistFiles.Enumerate(planConfig.NAS.PlaylistFolderUNC).ToDictionary(path=>Path.GetFileName(path)!,path=>path,StringComparer.OrdinalIgnoreCase);
            var sources=(System.Collections.Generic.Dictionary<string,string>)backupMethod.Invoke(null,new object[]{planConfig.NAS.PlaylistFolderUNC,visible,new[]{("Jazz.m3u",physicalHash)}})!;
            Check(!visible.ContainsKey("Jazz.m3u") && sources["Jazz.m3u"]==jazzPath,"Recovery includes a physical hidden target while the frozen planning baseline still excludes it");
            bool changedRefused=false;
            try{backupMethod.Invoke(null,new object[]{planConfig.NAS.PlaylistFolderUNC,visible,new[]{("Jazz.m3u","WRONG")}});}catch(TargetInvocationException error)when(error.InnerException is InvalidDataException){changedRefused=true;}
            Check(changedRefused,"Recovery preparation refuses target bytes that differ from the reviewed hash");
            Check(UserMessages.Explain(new InvalidDataException("Original file cannot reproduce device track count: Hootie & Smash Mouth.m3u")).Contains("Hootie & Smash Mouth.m3u"),"Visible failure keeps the actual playlist-specific reason");
            File.SetAttributes(jazzPath,FileAttributes.Normal);Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var savedRoot=Path.Combine(root,"SavedStartup");
            var savedConfig=new MediaMonkeyZidooSync.Models.AppConfig();
            savedConfig.Zidoo.ApiPort=9529;savedConfig.Zidoo.WolPort=9517;savedConfig.Zidoo.PlaylistGridColumns=5;
            savedConfig.Timing.ZidooReadinessTimeoutSeconds=120;savedConfig.Timing.ColdBootUiStabilizationSeconds=20;
            savedConfig.Application.ReportRoot=Path.Combine(savedRoot,"Reports");
            savedConfig.MediaMonkey.DatabasePath=Path.Combine(savedRoot,"offline.db");savedConfig.MediaMonkey.PlaylistParent="TEST";
            savedConfig.Zidoo.IPAddress="192.0.2.1";savedConfig.Zidoo.MacAddress="001122334455";savedConfig.Zidoo.ExpectedModel="TEST";savedConfig.Zidoo.BroadcastAddress="192.0.2.255";
            savedConfig.NAS.Host="offline.invalid";savedConfig.NAS.ShareName="test";savedConfig.NAS.MusicFolderName="music";savedConfig.NAS.PlaylistFolderName="lists";
            savedConfig.NAS.MusicRootUNC=@"\\offline.invalid\test\music";savedConfig.NAS.PlaylistFolderUNC=@"\\offline.invalid\test\lists";
            var correctPlaylistFolder=savedConfig.NAS.PlaylistFolderUNC;savedConfig.NAS.PlaylistFolderUNC=savedConfig.NAS.MusicRootUNC;
            ConfigurationService.Validate(savedConfig);
            Check(savedConfig.NAS.PlaylistFolderUNC==savedConfig.NAS.MusicRootUNC,"Configuration retains the frozen process's permission to use the same music and playlist folder");
            savedConfig.NAS.PlaylistFolderUNC=correctPlaylistFolder;
            new ConfigurationService(savedRoot).SaveAsync(savedConfig).GetAwaiter().GetResult();
            new UiStateService(Path.Combine(savedRoot,"ui-state.json")).SaveAsync(new MediaMonkeyZidooSync.Models.UiState{SelectedTabIndex=2}).GetAwaiter().GetResult();
            var savedWindow=new MainWindow(savedRoot);savedWindow.Show();Pump(500);
            Check(Field<TabControl>(savedWindow,"MainTabs").SelectedIndex==0,"Saved complete configuration always starts on Synchronization despite a saved History tab");
            Check(Field<TabItem>(savedWindow,"RunHistoryTab").IsEnabled && Field<Button>(savedWindow,"ValidateMediaMonkeyButton").IsEnabled,"Saved complete configuration enables other tabs and validation");
            Field<TabControl>(savedWindow,"MainTabs").SelectedItem=Field<TabItem>(savedWindow,"ConfigurationTab");
            Invoke(savedWindow,"SaveConfig_Click");Pump(500);
            Check(ReferenceEquals(Field<TabControl>(savedWindow,"MainTabs").SelectedItem,Field<TabItem>(savedWindow,"ConfigurationTab")),"Saving complete settings stays on Configuration");
            var savedHelp=new HelpWindow("Configuration");savedHelp.Show();Pump();
            var topics=Field<ListBox>(savedHelp,"Topics");
            Check(!topics.Items.Cast<ListBoxItem>().Any(item=>(string?)item.Tag=="ConfigurationFiles"),"Help leaves JSON reference details in the packaged README");
            savedHelp.Close();
            savedWindow.Close();Pump();
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




