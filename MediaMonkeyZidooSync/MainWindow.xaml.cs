// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.1 (Major/Minor controlled by the product owner)
// Development channel: Alpha.60.18.6
// Purpose: Primary application window, configuration presentation, synchronization launch, filtering, menus, and configuration archive UI.
// Remarks:
// - Version metadata is defined by MediaMonkeyZidooSync.csproj.
// - Released artifacts are immutable; any code change requires a new build identity.
// - Synchronization is entirely native C#; PowerShell is used only for build, test and installation tooling.
// ============================================================================
using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

namespace MediaMonkeyZidooSync;

/// <summary>
/// Main administrative UI. Presents synchronization, configuration, run-history placeholders,
/// live output filtering, Help, and configuration recovery without reimplementing backend sync mechanics.
/// </summary>
public partial class MainWindow : Window {
    private Window? _completionWindow;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Returns a standard Windows notification icon without a System.Drawing dependency.</summary>
    private static System.Windows.Media.ImageSource? GetWindowsNotificationIcon(MessageBoxImage icon)
    {
        uint stockId = icon == MessageBoxImage.Warning ? 78u :
                       icon == MessageBoxImage.Error ? 80u : 79u;
        const uint SHGSI_ICON = 0x000000100;

        var info = new SHSTOCKICONINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHSTOCKICONINFO>(),
            szPath = string.Empty
        };

        if (SHGetStockIconInfo(stockId, SHGSI_ICON, ref info) != 0 || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>Shows one independently activatable, modeless completion notification.</summary>
    private void ShowCompletionNotification(string message, string title, MessageBoxImage icon)
    {
        if (_completionWindow is not null)
        {
            _completionWindow.Close();
            _completionWindow = null;
        }

        var iconSource = GetWindowsNotificationIcon(icon);

        var window = new Window
        {
            Title = title,
            Width = 620,
            MinWidth = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = true
        };

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconImage = new Image
        {
            Source = iconSource,
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 2, 18, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Stretch = Stretch.Uniform
        };
        var messageBlock = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21 };
        Grid.SetColumn(messageBlock, 1);
        body.Children.Add(iconImage);
        body.Children.Add(messageBlock);
        root.Children.Add(body);

        var ok = new Button { Content = "OK", Width = 110, Height = 34, Margin = new Thickness(0,22,0,0), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        ok.Click += (_, _) => window.Close();
        Grid.SetRow(ok, 1);
        root.Children.Add(ok);
        window.Content = root;
        window.Closed += (_, _) => { if (ReferenceEquals(_completionWindow, window)) _completionWindow = null; };
        _completionWindow = window;
        window.Show();
        window.Activate();
    }

    private readonly ConfigurationService _configService;
    private readonly UiStateService _uiStateService = new();
    private UiState _uiState = new();
    private readonly SyncRunner _runner = new();
    private System.Threading.CancellationTokenSource? _syncCancellation;
    private readonly SyncRunner _nativeAssessmentRunner = new(
        new NativeSyncReadOnlyOrchestrator(
            new NativeSyncPlanProvider(new MediaMonkeyExportTransactionPlanner()),
            new NativePlaylistFolderReconciler()));
    // The inspect button and assessment button share the same XAML StackPanel.
    // A Window.FindName lookup can return null for controls in nested namescopes.
    // Resolve from the actual parent container and validate before changing state.
    private Button? FindInspectNativePlanButton()
    {
        return (RunNativeAssessmentButton.Parent as Panel)?.Children
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "InspectNativePlanButton");
    }

    private IReadOnlyList<NativePlanInspectionItem>? _lastNativePlanInspection;
    private System.Threading.CancellationTokenSource? _nativeAssessmentCancellation;
    private readonly ZidooDiscoveryService _zidooDiscovery = new();
    private readonly RunHistoryService _runHistoryService = new();
    private readonly MediaMonkeyPlaylistExporter _mediaMonkeyExporter = new();
    private readonly MediaMonkeyExporterEquivalenceService _exporterEquivalence = new();
    private readonly MediaMonkeyExportTransactionPlanner _transactionPlanner = new();
    private HelpWindow? _helpWindow;
    private Window? _nativePlanWindow;
    private readonly List<LogEvent> _events = new();
    private readonly SynchronizationWorkflow _workflow = new();
    private IReadOnlyList<NativeProductionSyncService.ReviewedChange>? _preparedReview, _approvedReview;
    private AppConfig? _config;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Stopwatch? _stopwatch;

    /// <summary>Gets the executing application directory.</summary>
    private string AppDirectory => AppContext.BaseDirectory;
/// <summary>Gets the packaged seed configuration path.</summary>
    private string SeedConfig => Path.Combine(AppDirectory, "Resources", "MediaMonkey-Zidoo-Config-v1.0.0.json");
/// <summary>Initializes UI services, configuration loading, live output handling, and elapsed-time display.</summary>
    public MainWindow() : this(IsolatedStartupMode.ResolveRoot()) { }

    /// <summary>Isolated configuration root for local UI verification; null uses normal application storage.</summary>
    public MainWindow(string? isolatedConfigRoot) {
        _configService = new ConfigurationService(isolatedConfigRoot);
        InitializeComponent();
        ConfigurationTab.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_,_) => InvalidateWorkflow()));
        RefreshWorkflowControls();
        if (IsolatedStartupMode.Enabled)
        {
            // Explicit smoke mode: no assessment, discovery, configuration mutation or sync.
            RunNativeAssessmentButton.IsEnabled = false;
            DiscoverZidooButton.IsEnabled = false;
            RunButton.IsEnabled = false;
        }
        // Apply consistent blank-row-area selection clearing to all read-only history grids.
        GridSelectionBehavior.Attach(ConfigHistoryGrid);
        GridSelectionBehavior.Attach(RunHistoryGrid);
        GridSelectionBehavior.Attach(RecoveryReportGrid);
        // Clear output selection when the user clicks elsewhere in the window.
        PreviewMouseDown += OutputWindow_PreviewMouseDown;
        PreviewMouseLeftButtonDown += MainWindow_ClearGridSelectionOutsideRows;
        Deactivated += (_, _) => ClearHistoryGridSelections();
        OutputRichText.LostKeyboardFocus += OutputRichText_LostKeyboardFocus;
        ProductVersionText.Text = $"Product {ApplicationVersionProvider.ProductVersion}";
        if (isolatedConfigRoot is null) _ = RestoreUiStateAsync();
        Loaded += async (_,__) => await LoadConfigurationAsync();
        _runner.Output += e => Dispatcher.InvokeAsync(() => { _events.Add(e); RefreshOutput(); });
        _nativeAssessmentRunner.Output += e => Dispatcher.InvokeAsync(() => { _events.Add(e); RefreshOutput(); });
        _timer.Tick += (_,__) => ElapsedText.Text = $"Elapsed: {FormatElapsed(_stopwatch?.Elapsed)}";
    }

    private void InvalidateWorkflow()
    {
        _workflow.Invalidate();_preparedReview=_approvedReview=null;
        _lastNativePlanInspection=null;_nativePlanWindow?.Close();
        RefreshWorkflowControls();
    }

    private void RefreshWorkflowControls()
    {
        if(RunButton is null || ReviewSynchronizationButton is null)return;
        bool allowed=!IsolatedStartupMode.Enabled;
        ValidateMediaMonkeyButton.IsEnabled=allowed && _workflow.CanValidate;
        VerifyNativeExporterButton.IsEnabled=allowed && _workflow.CanVerify;
        ReviewSynchronizationButton.IsEnabled=allowed && _workflow.CanReview && _preparedReview is not null;
        RunButton.IsEnabled=allowed && (_syncCancellation is not null ? !_syncCancellation.IsCancellationRequested : _workflow.CanRun);
        RunNativeAssessmentButton.IsEnabled=allowed && (_nativeAssessmentCancellation is not null || (_workflow.CanReview && _workflow.Verified));
        InspectNativePlanButton.IsEnabled=allowed && !_workflow.Busy && _lastNativePlanInspection is not null;
        ConfigurationTab.IsEnabled=!_workflow.Busy;
        WorkflowHint.Text=_workflow.NextStep;
    }

    private void ReviewSynchronization_Click(object sender,RoutedEventArgs e)
    {
        if(IsolatedStartupMode.Enabled || !_workflow.CanReview || _preparedReview is null)return;
        var review=_preparedReview;
        var window=new Window{Title="Review Synchronization Changes",Owner=this,Width=760,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new DockPanel{Margin=new Thickness(16)};
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var back=new Button{Content="Back",Padding=new Thickness(16,6,16,6)};
        var accept=new Button{Content="Use Reviewed Plan",Padding=new Thickness(16,6,16,6),Margin=new Thickness(10,0,0,0)};
        back.Click+=(_,_)=>window.DialogResult=false;accept.Click+=(_,_)=>window.DialogResult=true;
        footer.Children.Add(back);footer.Children.Add(accept);DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        var heading=new TextBlock{Text=review.Count==0?"No playlist changes are needed. This run will not contact or wake Zidoo.":
            "Review the proposed playlist changes below. Missing playlists may also be restored from your playlist files. After accepting, use Run Synchronization.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var details=review.Select(item=>(item.CandidateSHA256 is null?"Delete":item.OriginalSHA256 is null?"Create":"Update")+": "+
            Path.GetFileNameWithoutExtension(item.FileName)+(item.CandidateSHA256 is null?"":" ("+item.ExpectedCount+" tracks)"));
        layout.Children.Add(new TextBox{Text=string.Join(Environment.NewLine,details),IsReadOnly=true,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,TextWrapping=TextWrapping.Wrap,Padding=new Thickness(10)});
        window.Content=layout;
        if(window.ShowDialog()==true && ReferenceEquals(_preparedReview,review))
        {_approvedReview=review;_workflow.AcceptReview();RefreshWorkflowControls();}
    }

    /// <summary>Loads the current configuration and populates the Configuration tab.</summary>
    private async Task LoadConfigurationAsync() {
        try {
            if (IsolatedStartupMode.Enabled && !File.Exists(_configService.CurrentPath))
            {
                // Never seed a test from the packaged environment-specific configuration.
                var safe = new AppConfig();
                safe.Application.ReportRoot = Path.Combine(_configService.Root, "Reports");
                Directory.CreateDirectory(_configService.CurrentDirectory);
                File.WriteAllText(_configService.CurrentPath,
                    System.Text.Json.JsonSerializer.Serialize(safe));
            }
            _config = await _configService.LoadAsync(SeedConfig);
            ShowConfig();
            ConfigMessage.Text = "Configuration loaded.";
        } catch (Exception ex) { ConfigMessage.Text = UserMessages.Explain(ex); }
    }

    /// <summary>Copies the in-memory configuration into all editable UI fields and refreshes archive history.</summary>
    private void ShowConfig() {
        if (_config is null) return;
        ZidooIp.Text=_config.Zidoo.IPAddress; ZidooMac.Text=_config.Zidoo.MacAddress;
        ExpectedModel.Text=_config.Zidoo.ExpectedModel; ApiPort.Text=_config.Zidoo.ApiPort.ToString();
        Broadcast.Text=_config.Zidoo.BroadcastAddress; WolPort.Text=_config.Zidoo.WolPort.ToString();
        GridColumns.Text=_config.Zidoo.PlaylistGridColumns.ToString();
        MmDb.Text=_config.MediaMonkey.DatabasePath; PlaylistParent.Text=_config.MediaMonkey.PlaylistParent;
        NasHost.Text=_config.NAS.Host; NasShare.Text=_config.NAS.ShareName;
        MusicFolderName.Text=_config.NAS.MusicFolderName; PlaylistFolderName.Text=_config.NAS.PlaylistFolderName;
        NasMusic.Text=_config.NAS.MusicRootUNC; PlaylistFolder.Text=_config.NAS.PlaylistFolderUNC;
        ReportRoot.Text=_config.Application.ReportRoot;
        ReadinessTimeout.Text=_config.Timing.ZidooReadinessTimeoutSeconds.ToString();
        ColdBootSeconds.Text=_config.Timing.ColdBootUiStabilizationSeconds.ToString();
        RefreshHistory();
    }

    /// <summary>Copies editable UI fields into the in-memory configuration before validation/save/run.</summary>
    private void ReadConfigFromForm() {
        if (_config is null) throw new InvalidOperationException("Configuration is not loaded.");
        _config.Zidoo.IPAddress=ZidooIp.Text.Trim(); _config.Zidoo.MacAddress=ZidooMac.Text.Trim();
        _config.Zidoo.ExpectedModel=ExpectedModel.Text.Trim(); _config.Zidoo.ApiPort=int.Parse(ApiPort.Text);
        _config.Zidoo.BroadcastAddress=Broadcast.Text.Trim(); _config.Zidoo.WolPort=int.Parse(WolPort.Text);
        _config.Zidoo.PlaylistGridColumns=int.Parse(GridColumns.Text);
        _config.MediaMonkey.DatabasePath=MmDb.Text.Trim(); _config.MediaMonkey.PlaylistParent=PlaylistParent.Text.Trim();
        _config.NAS.Host=NasHost.Text.Trim(); _config.NAS.ShareName=NasShare.Text.Trim();
        _config.NAS.MusicFolderName=MusicFolderName.Text.Trim(); _config.NAS.PlaylistFolderName=PlaylistFolderName.Text.Trim();
        _config.NAS.MusicRootUNC=NasMusic.Text.Trim(); _config.NAS.PlaylistFolderUNC=PlaylistFolder.Text.Trim();
        _config.Application.ReportRoot=ReportRoot.Text.Trim();
        _config.Timing.ZidooReadinessTimeoutSeconds=int.Parse(ReadinessTimeout.Text);
        _config.Timing.ColdBootUiStabilizationSeconds=int.Parse(ColdBootSeconds.Text);
    }

    /// <summary>Validates, archives, and saves the current Configuration form values.</summary>
    private async void SaveConfig_Click(object sender, RoutedEventArgs e) {
        if (IsolatedStartupMode.Enabled) return;
        try { ReadConfigFromForm(); await _configService.SaveAsync(_config!); ConfigMessage.Text="Configuration validated, archived, and saved."; RefreshHistory(); }
        catch(Exception ex) { ConfigMessage.Text=$"Configuration was not saved: {UserMessages.Explain(ex)}"; }
    }

    /// <summary>Restores Last Known Good and reloads the Configuration form.</summary>
    private async void RestoreConfig_Click(object sender, RoutedEventArgs e) {
        try { _configService.RestoreLastKnownGood(); _config=await _configService.LoadAsync(SeedConfig); ShowConfig(); ConfigMessage.Text="Last Known Good configuration restored. Previous current configuration was archived."; RefreshHistory(); }
        catch(Exception ex) { ConfigMessage.Text=$"Restore failed: {UserMessages.Explain(ex)}"; }
    }

    /// <summary>Displays only operations from the last completed read-only assessment.</summary>
    private void InspectNativePlan_Click(object sender, RoutedEventArgs e)
    {
        var items = _lastNativePlanInspection;
        if (items is null) return;
        // Reuse an existing inspector rather than stacking duplicate modeless windows.
        if (_nativePlanWindow is not null)
        {
            if (_nativePlanWindow.WindowState == WindowState.Minimized)
                _nativePlanWindow.WindowState = WindowState.Normal;
            _nativePlanWindow.Activate();
            return;
        }
        var window = new Window
        {
            Title = "Native Synchronization Plan — Read-only",
            // Intentionally unowned: an owned WPF window stays above its owner.
            // Both the main application and inspector must be independently activatable.
            Width = 1000, Height = 560,
            MinWidth = 640, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.Manual
        };
        // Center over the main window without establishing an owner relationship.
        window.Left = Left + Math.Max(0, (ActualWidth - window.Width) / 2);
        window.Top = Top + Math.Max(0, (ActualHeight - window.Height) / 2);
        var layout = new DockPanel { Margin = new Thickness(14), Background = Brushes.Transparent };
        var heading = new TextBlock
        {
            Text = $"Proposed changes: {items.Count} | No files will be modified. Unchanged playlists are included in assessment totals, not this change list.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        var filter = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        // Display labels are independent from the existing operation identifiers.
        var filterOptions = new[]
        {
            new { Label = "All proposed changes", Value = "" },
            new { Label = "Create", Value = "Create" },
            new { Label = "Update", Value = "Update" },
            new { Label = "Pending Zidoo Delete", Value = "PendingZidooDelete" }
        };
        filter.ItemsSource = filterOptions;
        filter.DisplayMemberPath = "Label";
        filter.SelectedValuePath = "Value";
        filter.SelectedIndex = 0;
        DockPanel.SetDock(filter, Dock.Top);
        layout.Children.Add(filter);
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var copy = new Button { Content = "Copy Selected", Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed };
        var close = new Button { Content = "Close", Padding = new Thickness(24, 7, 24, 7), IsCancel = true };
        bottom.Children.Add(copy);
        bottom.Children.Add(close);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(bottom);
        var table = new DataGrid
        {
            IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        table.Columns.Add(new DataGridTextColumn { Header = "Operation", Binding = new System.Windows.Data.Binding("Action"), Width = new DataGridLength(175) });
        table.Columns.Add(new DataGridTextColumn { Header = "Playlist file", Binding = new System.Windows.Data.Binding("FileName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Reason", Binding = new System.Windows.Data.Binding("Explanation"), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        void CopySelected()
        {
            var rows = table.SelectedItems.Cast<NativePlanInspectionItem>().ToArray();
            if (rows.Length == 0) return;
            Clipboard.SetText(string.Join(Environment.NewLine,
                rows.Select(x => $"{x.Action}\t{x.FileName}\t{x.Explanation}")));
        }
        copy.Click += (_, _) => CopySelected();
        close.Click += (_, _) => window.Close();
        var menu = new ContextMenu();
        var copyMenu = new MenuItem { Header = "Copy Selected Rows" };
        copyMenu.Click += (_, _) => CopySelected();
        menu.Items.Add(copyMenu);
        var selectAllMenu = new MenuItem { Header = "Select All" };
        selectAllMenu.Click += (_, _) => table.SelectAll();
        menu.Items.Add(selectAllMenu);
        table.ContextMenu = menu;
        // Keep the copy action out of sight until at least one row is selected.
        void RefreshSelectionActions()
        {
            bool hasSelection = table.SelectedItems.Count > 0;
            copy.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            copyMenu.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            selectAllMenu.IsEnabled = table.Items.Count > 0;
        }
        table.SelectionChanged += (_, _) => RefreshSelectionActions();
        table.ContextMenuOpening += (_, _) => RefreshSelectionActions();
        // Use the same blank-area selection behavior as the main application grids.
        GridSelectionBehavior.Attach(table);
        filter.SelectionChanged += (_, _) =>
        {
            // A new filter must never retain a selection from the previous view.
            table.UnselectAll();
            var chosen = filter.SelectedValue as string;
            table.ItemsSource = string.IsNullOrEmpty(chosen) ? items :
                items.Where(x => x.Action.ToString() == chosen).ToArray();
            RefreshSelectionActions();
        };
        table.ItemsSource = items;
        RefreshSelectionActions();
        layout.Children.Add(table);
        // A transparent content surface receives clicks on otherwise empty space.
        // Register on the window itself so clicks on the heading, filter, footer,
        // and blank content are all observed, including handled child events.
        window.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, args) =>
            {
                if (args.OriginalSource is not DependencyObject source) return;
                static bool IsWithin(DependencyObject node, DependencyObject target)
                {
                    for (DependencyObject? current = node; current is not null;)
                    {
                        if (ReferenceEquals(current, target)) return true;
                        current = current is System.Windows.Media.Visual ||
                                  current is System.Windows.Media.Media3D.Visual3D
                            ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                            : System.Windows.LogicalTreeHelper.GetParent(current);
                    }
                    return false;
                }
                // Keep selection intact for grid gestures and the copy action.
                if (IsWithin(source, table) || IsWithin(source, copy)) return;
                table.UnselectAll();
            }), true);

        // Modeless inspector permits File/Help menus and other main-window actions.
        // Clear selection when returning to the owner, not when switching to an
        // unrelated application. Never treat loss of focus alone as a click.
        EventHandler ownerActivated = (_, _) => table.UnselectAll();
        Activated += ownerActivated;
        window.Closed += (_, _) =>
        {
            Activated -= ownerActivated;
            if (ReferenceEquals(_nativePlanWindow, window)) _nativePlanWindow = null;
        };
        window.Content = layout;
        _nativePlanWindow = window;
        window.Show();
        window.Activate();
    }

    /// <summary>Read-only assessment. Does not save configuration or authorize any mutation.</summary>
    private async void RunNativeAssessment_Click(object sender, RoutedEventArgs e)
    {
        if (IsolatedStartupMode.Enabled) return;
        if (_nativeAssessmentCancellation is not null)
        {
            // Repeated cancellation requests are harmless, including during shutdown.
            if (!_nativeAssessmentCancellation.IsCancellationRequested)
            {
                _nativeAssessmentCancellation.Cancel();
                StatusText.Text = "Canceling native assessment...";
            }
            return;
        }
        if(_workflow.Busy || !_workflow.Verified)return;
        _workflow.SetDiagnosticBusy(true);RefreshWorkflowControls();
        // Validate required UI dependencies before mutating assessment state.
        // A missing control is a recoverable initialization error, not a reason to crash.
        Button? inspectButton;
        try
        {
            inspectButton = FindInspectNativePlanButton();
            if (inspectButton is null)
                throw new InvalidOperationException(
                    "The Inspect Plan control could not be initialized. Restart the application; " +
                    "if the issue persists, check the diagnostic log and application installation.");
        }
        catch (Exception ex)
        {
            _workflow.SetDiagnosticBusy(false);RefreshWorkflowControls();
            StatusText.Text = "Native assessment unavailable";
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Red,
                "Unable to start read-only native assessment: " + ex));
            RefreshOutput();
            MessageBox.Show(this, UserMessages.Explain(ex), "Native Assessment Unavailable",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        // A new assessment invalidates any previously displayed proposed changes.
        // Closing the old inspector also releases its event subscriptions.
        _nativePlanWindow?.Close();
        var started = DateTime.Now;
        bool showCompletedPlan = false;
        _nativeAssessmentCancellation = new System.Threading.CancellationTokenSource();
        // Keep the token local so a closing window cannot replace the token mid-await.
        var assessmentToken = _nativeAssessmentCancellation.Token;RefreshWorkflowControls();
        RunNativeAssessmentButton.Content = "Cancel Native Assessment";
        RunButton.IsEnabled = false;
        ValidateMediaMonkeyButton.IsEnabled = false;
        VerifyNativeExporterButton.IsEnabled = false;
        _lastNativePlanInspection = null;
        inspectButton.IsEnabled = false;
        _events.Clear();
        RefreshOutput();
        _stopwatch = Stopwatch.StartNew();
        _timer.Start();
        string resultLabel = "Assessment failed";
        try
        {
            // Capture the current form without saving configuration or invoking the sync runner.
            ReadConfigFromForm();
            var config = _config ?? throw new InvalidOperationException("Configuration is not loaded.");
            StatusText.Text = "Native assessment running (read-only)";
            var result = await _nativeAssessmentRunner.AssessAsync(config, assessmentToken);
            _lastNativePlanInspection = NativePlanInspection.FromAssessment(result);
            inspectButton.IsEnabled = true;
            showCompletedPlan = true;
            resultLabel = $"Read-only: {result.CreateCount} create, {result.UpdateCount} update, {result.UnchangedCount} unchanged, {result.PendingZidooDeleteCount} pending deletion";
            StatusText.Text = "Native assessment completed (read-only)";
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Green, resultLabel + ". No changes executed."));
            RefreshOutput();
        }
        catch (OperationCanceledException) when (assessmentToken.IsCancellationRequested)
        {
            resultLabel = "Assessment canceled";
            StatusText.Text = resultLabel;
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Yellow, resultLabel + ". No changes executed."));
            RefreshOutput();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Native assessment failed";
            DiagnosticReportingService.Shared.Report("Native read-only assessment", ex);
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Red, "Read-only native assessment failed: " + ex));
            RefreshOutput();
        }
        finally
        {
            _timer.Stop();
            _stopwatch?.Stop();
            _nativeAssessmentCancellation?.Dispose();
            _nativeAssessmentCancellation = null;
            RunNativeAssessmentButton.Content = "Run Native Assessment (Read-only)";
            RunButton.IsEnabled = true;
            ValidateMediaMonkeyButton.IsEnabled = true;
            VerifyNativeExporterButton.IsEnabled = true;
            // A failed or cancelled assessment must not leave a stale inspect action enabled.
            inspectButton.IsEnabled = _lastNativePlanInspection is not null;
            _workflow.SetDiagnosticBusy(false);RefreshWorkflowControls();
            RecordHistory(started, "Native Read-only Assessment", resultLabel);
        }
        // Present the validated proposal automatically, only after successful completion
        // and after restoring assessment controls. Display errors are non-fatal to the
        // already-completed read-only assessment and must be visible to the user.
        if (showCompletedPlan && _lastNativePlanInspection is not null)
        {
            try
            {
                InspectNativePlan_Click(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                StatusText.Text = "Assessment completed; plan window unavailable";
                _events.Add(new LogEvent(DateTime.Now, LogLevel.Red,
                    "Read-only assessment succeeded, but the plan window could not open: " + ex));
                RefreshOutput();
                MessageBox.Show(this,
                    "The read-only assessment completed successfully, but its proposed-change window could not be displayed. " +
                    "Use Inspect Plan to try again. Details: " + UserMessages.Explain(ex),
                    "Plan Inspection Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
    /// <summary>Saves the current configuration, launches synchronization, updates status, and marks successful configuration Last Known Good.</summary>
    private async void Run_Click(object sender, RoutedEventArgs e) {
        if (IsolatedStartupMode.Enabled) return;
        if(_syncCancellation is not null){_syncCancellation.Cancel();RunButton.IsEnabled=false;StatusText.Text="Cancelling; finishing recovery";return;}
        if(!RunButton.IsEnabled || !_workflow.CanRun || _approvedReview is null)return;
        var review=_approvedReview;_workflow.BeginRun();RefreshWorkflowControls();
        _syncCancellation=new System.Threading.CancellationTokenSource();
        var token=_syncCancellation.Token;RefreshWorkflowControls();
        RunButton.Content="Cancel Synchronization"; StatusText.Text="Preparing changes"; _events.Clear(); RefreshOutput();
        ValidateMediaMonkeyButton.IsEnabled=false;VerifyNativeExporterButton.IsEnabled=false;RunNativeAssessmentButton.IsEnabled=false;
        DateTime runStarted = DateTime.Now;
        _stopwatch=Stopwatch.StartNew(); _timer.Start();
        try {
            ReadConfigFromForm();
            var config = _config ?? throw new InvalidOperationException("Configuration was not created from the form.");
            await _configService.SaveAsync(config);
            token.ThrowIfCancellationRequested();StatusText.Text="Running";
            int rc=await _runner.RunAsync(config,token,review);
            if(rc==0) {
                StatusText.Text=_runner.LastRunRecovered?"Recovery completed; validate again":"Completed";
                if(_runner.LastRunVerifiedDevice)
                {
                    try{_configService.MarkLastKnownGood();ConfigMessage.Text="Successful verified run: current configuration marked Last Known Good.";}
                    catch(Exception backupError){ConfigMessage.Text="Playlists synchronized, but the configuration backup could not be saved.";_events.Add(new LogEvent(DateTime.Now,LogLevel.Yellow,ConfigMessage.Text){DiagnosticDetails=backupError.ToString()});}
                }
                else ConfigMessage.Text=_runner.LastRunRecovered?"Recovery completed; existing Last Known Good configuration retained.":"No playlist changes; existing Last Known Good configuration retained.";
                _events.Add(_runner.LastRunRecovered
                    ? new LogEvent(DateTime.Now,LogLevel.Green,"Interrupted synchronization recovered and verified. Start with Validate MediaMonkey to review the current changes.")
                    : UserMessages.Completed(_runner.LastRunCompletedChanges));RefreshOutput();
            }
            else StatusText.Text=$"Failed ({rc})";
        } catch(OperationCanceledException error) when(token.IsCancellationRequested) {
            StatusText.Text="Cancelled";
            _events.Add(UserMessages.Cancelled(error));RefreshOutput();
        } catch(Exception ex) {
            StatusText.Text="Failed"; _events.Add(UserMessages.Failure(ex)); RefreshOutput();
        } finally {
            _timer.Stop(); _stopwatch?.Stop();
            _syncCancellation?.Dispose();_syncCancellation=null;
            RunButton.Content="4. Run Synchronization";RunButton.IsEnabled=true;
            ValidateMediaMonkeyButton.IsEnabled=true;VerifyNativeExporterButton.IsEnabled=true;RunNativeAssessmentButton.IsEnabled=true;
            _workflow.EndRun();_preparedReview=_approvedReview=null;RefreshWorkflowControls();
            RecordHistory(runStarted, "Synchronization", StatusText.Text);
        }
    }



    /// <summary>
    /// Runs the native MediaMonkey reader/exporter in validation-only mode.
    /// This reads MM5.DB and validates every managed track but does not write/delete M3U files
    /// and does not contact or wake Zidoo.
    /// </summary>
    private async void ValidateMediaMonkey_Click(object sender, RoutedEventArgs e)
    {
        if(IsolatedStartupMode.Enabled || !_workflow.CanValidate)return;
        _workflow.BeginValidation();_preparedReview=_approvedReview=null;RefreshWorkflowControls();
        ValidateMediaMonkeyButton.IsEnabled = false;
        RunButton.IsEnabled = false;
        StatusText.Text = "Validating MediaMonkey playlists";
        _events.Clear();
        RefreshOutput();
        _stopwatch = Stopwatch.StartNew();
        _timer.Start();

        DateTime runStarted = DateTime.Now;
        MediaMonkeyPlaylistExportResult? result = null;
        Exception? failure = null;

        try
        {
            ReadConfigFromForm();
            var config = _config ?? throw new InvalidOperationException("Configuration is not loaded.");

            _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                "Starting MediaMonkey playlist validation."));
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                "The application is reading the MediaMonkey database in read-only mode. It will not contact Zidoo or change playlist files."));
            RefreshOutput();

            // File-system validation performs many synchronous existence checks, often
            // against network storage. Run the validator on a worker thread so WPF
            // remains responsive and the elapsed-time display continues to update.
            result = await Task.Run(() => _mediaMonkeyExporter.ValidateAsync(config));

            _events.Add(new LogEvent(DateTime.Now, LogLevel.Green,
                "MediaMonkey playlist validation completed successfully."));
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Green,
                $"Playlists: {result.PlaylistCount:N0}  |  Playlist entries: {result.TrackCount:N0}  |  Empty playlists: {result.EmptyPlaylistCount:N0}"));
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                $"Playlist map report: {result.PlaylistMapReportPath}"));
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                $"Summary report: {result.SummaryReportPath}"));
            StatusText.Text = "MediaMonkey validation passed";
            RefreshOutput();
        }
        catch (Exception ex)
        {
            failure = ex;
            StatusText.Text = "MediaMonkey validation failed";
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Red,
                "MediaMonkey playlist validation did not complete."));
            _events.Add(UserMessages.Failure(ex));
            RefreshOutput();
        }
        finally
        {
            _timer.Stop();
            _stopwatch?.Stop();
            ElapsedText.Text = $"Elapsed: {FormatElapsed(_stopwatch?.Elapsed)}";
            ValidateMediaMonkeyButton.IsEnabled = true;
            RunButton.IsEnabled = true;
            _workflow.EndValidation(failure is null && result is not null);RefreshWorkflowControls();
            RecordHistory(runStarted, "MediaMonkey Validation", StatusText.Text);
        }

        if (result is not null)
        {
            ShowCompletionNotification(
                $"MediaMonkey playlist validation completed successfully.\n\n" +
                $"Playlists checked: {result.PlaylistCount:N0}\n" +
                $"Playlist entries checked: {result.TrackCount:N0}\n" +
                $"Empty playlists: {result.EmptyPlaylistCount:N0}\n" +
                $"Elapsed time: {FormatElapsed(_stopwatch?.Elapsed)}\n\n" +
                "No playlist files were changed and Zidoo was not contacted.",
                "MediaMonkey Validation Complete",
                MessageBoxImage.Information);
        }
        else if (failure is not null)
        {
            ShowCompletionNotification(
                $"MediaMonkey playlist validation did not complete.\n\n{UserMessages.Explain(failure)}",
                "MediaMonkey Validation",
                MessageBoxImage.Error);
        }
    }


    /// <summary>
    /// Performs a read-only equivalence check between the native exporter's exact
    /// in-memory M3U output and the existing authoritative M3U mirror.
    /// </summary>
    private async void VerifyNativeExporter_Click(object sender, RoutedEventArgs e)
    {
        if(IsolatedStartupMode.Enabled || !_workflow.CanVerify)return;
        _workflow.BeginVerification();_preparedReview=_approvedReview=null;RefreshWorkflowControls();
        VerifyNativeExporterButton.IsEnabled = false;
        ValidateMediaMonkeyButton.IsEnabled = false;
        RunButton.IsEnabled = false;
        StatusText.Text = "Verifying native exporter";
        _events.Clear();
        RefreshOutput();
        _stopwatch = Stopwatch.StartNew();
        _timer.Start();

        DateTime runStarted = DateTime.Now;
        MediaMonkeyExporterEquivalenceResult? result = null;
        MediaMonkeyExportTransactionPlan? plan = null;
        Exception? failure = null;

        try
        {
            ReadConfigFromForm();
            var config = _config ?? throw new InvalidOperationException("Configuration is not loaded.");

            _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                "Starting read-only native exporter equivalence verification."));
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                "The application will compare the native exporter's expected M3U files with the existing playlist files. It will not change playlist files or contact Zidoo."));
            RefreshOutput();

            result = await Task.Run(() => _exporterEquivalence.VerifyAsync(config));
            plan = await Task.Run(() => _transactionPlanner.PrepareAsync(config));
            if (plan.CreateCount != result.MissingPlaylistFileCount ||
                plan.UpdateCount != result.ContentDifferencePlaylistCount ||
                plan.UnchangedCount != result.MatchingPlaylistCount ||
                plan.PendingZidooDeleteCount != System.Linq.Enumerable.Count(result.Differences, x => x.Problem == "Existing authoritative mirror contains an M3U that the native exporter does not expect."))
                throw new InvalidDataException("The native transaction plan does not match the exporter comparison. No changes were made.");
            _preparedReview = await _runner.PrepareReviewAsync(config);
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                $"Transaction plan (read-only): Unchanged {plan.UnchangedCount:N0}  |  Create {plan.CreateCount:N0}  |  Update {plan.UpdateCount:N0}  |  Pending Zidoo deletion {plan.PendingZidooDeleteCount:N0}"));

            if (result.Equivalent)
            {
                _events.Add(new LogEvent(DateTime.Now, LogLevel.Green,
                    "Native exporter equivalence verification passed."));
                _events.Add(new LogEvent(DateTime.Now, LogLevel.Green,
                    $"Expected playlists: {result.ExpectedPlaylistCount:N0}  |  Existing playlist files: {result.ExistingPlaylistCount:N0}  |  Playlists with differences: 0"));
                StatusText.Text = "Native exporter verified";
            }
            else
            {
                _events.Add(new LogEvent(DateTime.Now, LogLevel.Yellow,
                    "Exporter verification completed. Playlist changes are ready to review."));
                _events.Add(new LogEvent(DateTime.Now, LogLevel.Red,
                    $"Expected playlists: {result.ExpectedPlaylistCount:N0}  |  Existing playlist files: {result.ExistingPlaylistCount:N0}  |  Playlists with differences: {result.PlaylistsWithDifferencesCount:N0}"));
                _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                    $"Matching existing playlists: {result.MatchingPlaylistCount:N0}  |  Missing playlist files: {result.MissingPlaylistFileCount:N0}  |  Existing playlists with content differences: {result.ContentDifferencePlaylistCount:N0}"));
                StatusText.Text = "Playlist differences detected";
            }

            _events.Add(new LogEvent(DateTime.Now, LogLevel.Blue,
                $"Equivalence report: {result.ReportPath}"));
            RefreshOutput();
        }
        catch (Exception ex)
        {
            failure = ex;
            StatusText.Text = "Native exporter verification failed";
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Red,
                "Native exporter equivalence verification did not complete."));
            _events.Add(UserMessages.Failure(ex));
            RefreshOutput();
        }
        finally
        {
            _timer.Stop();
            _stopwatch?.Stop();
            ElapsedText.Text = $"Elapsed: {FormatElapsed(_stopwatch?.Elapsed)}";
            VerifyNativeExporterButton.IsEnabled = true;
            ValidateMediaMonkeyButton.IsEnabled = true;
            RunButton.IsEnabled = true;
            _workflow.EndVerification(failure is null && result is not null && plan is not null && _preparedReview is not null);RefreshWorkflowControls();
            RecordHistory(runStarted, "Native Exporter Verification", StatusText.Text);
        }

        if (result is not null && plan is not null)
        {
            string message = result.Equivalent
                ? $"Native exporter verification completed successfully.\n\n" +
                  $"Expected playlists: {result.ExpectedPlaylistCount:N0}\n" +
                  $"Existing M3U files: {result.ExistingPlaylistCount:N0}\n" +
                  $"Playlist files compared: {result.ComparedPlaylistCount:N0}\n" +
                  $"Differences: 0\n" +
                  $"Transaction plan: Unchanged {plan.UnchangedCount:N0} | Create {plan.CreateCount:N0} | Update {plan.UpdateCount:N0} | Pending Zidoo deletion {plan.PendingZidooDeleteCount:N0}\n" +
                  $"Elapsed time: {FormatElapsed(_stopwatch?.Elapsed)}\n\n" +
                  "Every expected playlist filename and every playlist line matched the existing authoritative playlist files.\n\n" +
                  "No playlist files were changed and Zidoo was not contacted."
                : $"Native exporter verification completed. Playlist differences were detected.\n\n" +
                  $"Expected playlists: {result.ExpectedPlaylistCount:N0}\n" +
                  $"Existing playlist files: {result.ExistingPlaylistCount:N0}\n" +
                  $"Matching existing playlists: {result.MatchingPlaylistCount:N0}\n" +
                  $"Playlists with differences: {result.PlaylistsWithDifferencesCount:N0}\n" +
                  $"Missing playlist files: {result.MissingPlaylistFileCount:N0}\n" +
                  $"Existing playlists with content differences: {result.ContentDifferencePlaylistCount:N0}\n" +
                  $"Transaction plan: Unchanged {plan.UnchangedCount:N0} | Create {plan.CreateCount:N0} | Update {plan.UpdateCount:N0} | Pending Zidoo deletion {plan.PendingZidooDeleteCount:N0}\n" +
                  $"Elapsed time: {FormatElapsed(_stopwatch?.Elapsed)}\n\n" +
                  $"Review the detailed equivalence report:\n{result.ReportPath}\n\n" +
                  "No playlist files were changed and Zidoo was not contacted.";

            ShowCompletionNotification(
                message,
                result.Equivalent ? "Native Exporter Verified" : "Playlist Differences Detected",
                result.Equivalent ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        else if (failure is not null)
        {
            ShowCompletionNotification(
                $"Native exporter verification did not complete.\n\n{UserMessages.Explain(failure)}",
                "Native Exporter Verification",
                MessageBoxImage.Error);
        }
    }

    private void RecordHistory(DateTime started, string operation, string result)
    {
        try
        {
            string root = _config?.Application.ReportRoot ?? ReportRoot.Text.Trim();
            _runHistoryService.Save(root, started, operation, result,
                _stopwatch?.Elapsed ?? TimeSpan.Zero, _events);
            RefreshRunHistory();
        }
        catch (Exception ex)
        {
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Yellow,
                $"Run history could not be saved: {UserMessages.Explain(ex)}"));
            RefreshOutput();
        }
    }

    private void RefreshRunHistory()
    {
        if (RunHistoryGrid is null || RunHistoryStatus is null) return;
        try
        {
            string root = _config?.Application.ReportRoot ?? ReportRoot.Text.Trim();
            var records = _runHistoryService.List(root);
            RunHistoryGrid.ItemsSource = records;
            RunHistoryStatus.Text = records.Count == 0
                ? "No structured runs recorded yet. Validate or Verify to create the first entry."
                : $"{records.Count} recorded runs (newest first).";
        }
        catch (Exception ex) { RunHistoryStatus.Text = $"Could not read run history: {UserMessages.Explain(ex)}"; }
    }

    private void RefreshRecoveryReports_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string root = _config?.Application.ReportRoot ?? ReportRoot.Text.Trim();
            // Explicitly separate from the normal run history and do not create directories.
            string directory = Path.Combine(root, "RecoveryPlanReports");
            var rows = new IsolatedRecoveryReportBrowserService().List(directory);
            RecoveryReportGrid.ItemsSource = rows;
            RecoveryReportSummary.Text = FormatRecoveryReportSummary(rows);
            RecoveryReportStatus.Text = rows.Count == 0
                ? "No recovery plan reports found in RecoveryPlanReports. Read-only mode; no files changed. Recovery execution disabled."
                : "Saved recovery reports loaded. Read-only mode; no files changed. Recovery execution disabled.";
        }
        catch (Exception ex)
        {
            RecoveryReportGrid.ItemsSource = null;
            RecoveryReportSummary.Text = "Report counts unavailable";
            RecoveryReportStatus.Text = $"Recovery report review unavailable: {UserMessages.Explain(ex)}. Read-only mode; recovery execution disabled.";
        }
    }

    private static string FormatRecoveryReportSummary(System.Collections.Generic.IReadOnlyList<RecoveryReportRow> rows)
    {
        var summary = RecoveryReportDisplaySummary.From(rows);
        return $"Reports: {summary.Total} | Require review: {summary.NeedsReview} | " +
               $"Pending: {summary.Pending} | Other: {summary.Other}";
    }

    private void RefreshRunHistory_Click(object sender, RoutedEventArgs e) => RefreshRunHistory();

    private void OpenRunLog_Click(object sender, RoutedEventArgs e)
    {
        if (RunHistoryGrid.SelectedItem is not RunHistoryService.RunRecord selected)
        {
            RunHistoryStatus.Text = "Select a completed run first.";
            return;
        }
        try
        {
            if (!File.Exists(selected.LogPath))
                throw new FileNotFoundException("The selected run log is unavailable.");
            Process.Start(new ProcessStartInfo(selected.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex) { RunHistoryStatus.Text = $"Could not open log: {UserMessages.Explain(ex)}"; }
    }

    /// <summary>Refreshes the Configuration Archive grid from persistent archive metadata.</summary>
    private void RefreshHistory() {
        if (ConfigHistoryGrid is null) return;
        ConfigHistoryGrid.ItemsSource = _configService.GetArchiveHistory();
        RefreshRunHistory();
    }

    /// <summary>Handles the Refresh History button.</summary>
    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    /// <summary>Restores the archive selected by the user and reloads the Configuration form.</summary>
    private async void RestoreArchive_Click(object sender, RoutedEventArgs e) {
        try {
            if (ConfigHistoryGrid.SelectedItem is not ConfigurationArchiveEntry selected) {
                ConfigMessage.Text="Select an archived configuration to restore.";
                return;
            }
            _configService.RestoreArchive(selected);
            _config=await _configService.LoadAsync(SeedConfig);
            ShowConfig();
            ConfigMessage.Text=$"Archived configuration restored: {selected.Timestamp:g}. The previous current configuration was archived first.";
        } catch(Exception ex) { ConfigMessage.Text=$"Archive restore failed: {UserMessages.Explain(ex)}"; }
    }

    /// <summary>Selects Synchronization and invokes the same Run operation as the Run button.</summary>
    private void MenuRun_Click(object sender, RoutedEventArgs e) { MainTabs.SelectedIndex=0; Run_Click(sender,e); }
    /// <summary>Selects the Configuration tab.</summary>
    private void MenuConfiguration_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex=1;
    /// <summary>Selects the Run History / Logs tab.</summary>
    private void MenuHistory_Click(object sender, RoutedEventArgs e) { MainTabs.SelectedIndex=2; RefreshRunHistory(); }

    /// <summary>Opens Help at Getting Started.</summary>
    private void HelpGuide_Click(object sender, RoutedEventArgs e) => ShowHelp("GettingStarted");

    /// <summary>Opens the modeless Help window directly at Troubleshooting.</summary>
    private void Troubleshooting_Click(object sender, RoutedEventArgs e) => ShowHelp("Troubleshooting");

    /// <summary>Shows one independent, modeless Help window without forcing it above the main window.</summary>
    private void ShowHelp(string topic)
    {
        try
        {
            if (_helpWindow is null)
            {
                var window = new HelpWindow(topic);
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_helpWindow, window)) _helpWindow = null;
                };
                window.Show();
                _helpWindow = window;
            }
            else
            {
                _helpWindow.SelectTopic(topic);
                if (_helpWindow.WindowState == WindowState.Minimized)
                    _helpWindow.WindowState = WindowState.Normal;
                _helpWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            // Help is optional UI; failure must not disrupt the main application.
            Debug.WriteLine($"Help window could not be opened: {ex}");
            MessageBox.Show(this, $"Help could not be opened: {UserMessages.Explain(ex)}",
                "Help Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Performs conservative read-only discovery. The Zidoo must already be powered on.
    /// Discovery uses only active directly connected IPv4 subnets and documented API port 9529.
    /// </summary>
    private async void DiscoverZidoo_Click(object sender, RoutedEventArgs e)
    {
        if (IsolatedStartupMode.Enabled) return;
        DiscoverZidooButton.IsEnabled=false;
        DiscoveryStatus.Text="Searching. Make sure the Zidoo is powered on and connected to this local network...";
        try
        {
            var progress = new Progress<string>(message => DiscoveryStatus.Text=message);
            var devices = await _zidooDiscovery.DiscoverAsync(progress);
            if (devices.Count == 0)
            {
                DiscoveryStatus.Text="No Zidoo was found. Make sure the Zidoo is powered on and connected to the same local network, then try again. Manual configuration remains available.";
                ShowCompletionNotification(
                    $"No Zidoo device was identified.\n\nIt may be powered off, unreachable, or not identifiable through discovery.",
                    "Zidoo Discovery — No Device Found", MessageBoxImage.Warning);
                return;
            }

            ZidooDiscoveryResult? selected;
            if (devices.Count == 1) selected=devices[0];
            else
            {
                var chooser=new ZidooDiscoveryWindow(devices) { Owner=this };
                selected=chooser.ShowDialog()==true ? chooser.SelectedDevice : null;
            }
            if (selected is null) { DiscoveryStatus.Text="Discovery completed; no device was selected."; return; }

            ZidooIp.Text=selected.IPAddress;
            ZidooMac.Text=selected.MacAddress;
            ExpectedModel.Text=selected.Model;
            ApiPort.Text=ZidooDiscoveryService.DocumentedApiPort.ToString();
            DiscoveryStatus.Text=selected.IsValidatedModel
                ? $"Found {selected.Model} at {selected.IPAddress}. Configuration fields were populated; choose Save Configuration to commit them."
                : $"Found Zidoo model {selected.Model} at {selected.IPAddress}. This model has not been validated by this project. Review before saving.";
            ShowCompletionNotification($"Discovery completed.\n\nModel: {selected.Model}\nIP address: {selected.IPAddress}\nWired MAC address: {selected.MacAddress}\nFirmware: {selected.Firmware}\nValidated model: {(selected.IsValidatedModel ? "Yes" : "No")}\n\nReview the populated fields and select Save Configuration to save them.", "Zidoo Discovery Completed", selected.IsValidatedModel ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch(Exception ex)
        {
            DiagnosticReportingService.Shared.Report("Discover Zidoo", ex);
            _events.Add(new LogEvent(DateTime.Now, LogLevel.Red, $"Discover Zidoo failed: {UserMessages.Explain(ex)}"));
            RefreshOutput();
            DiscoveryStatus.Text=$"Discovery could not complete: {UserMessages.Explain(ex)}";
            ShowCompletionNotification($"Discovery could not complete.\n\n{UserMessages.Explain(ex)}", "Zidoo Discovery Error", MessageBoxImage.Error);
        }
        finally { DiscoverZidooButton.IsEnabled=true; }
    }

    /// <summary>Displays product and runtime information.</summary>
    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show($"MediaMonkey Zidoo Sync\nProduct {ApplicationVersionProvider.ProductVersion}\nAssembly {Assembly.GetExecutingAssembly().GetName().Version}\n\nMediaMonkey-managed playlist synchronization through the validated playlist-folder/Zidoo Music workflow.\n\n.NET: {Environment.Version}\nOS: {Environment.OSVersion}",
        "About MediaMonkey Zidoo Sync",MessageBoxButton.OK,MessageBoxImage.Information);

    /// <summary>Closes the application.</summary>
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Reapplies presentation-only live-output filters.</summary>
    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshOutput();

    /// <summary>Returns whether a semantic log category is currently enabled for display.</summary>
    private bool Visible(LogLevel l) => l switch {
        LogLevel.Blue => ShowBlue.IsChecked==true, LogLevel.Yellow => ShowYellow.IsChecked==true,
        LogLevel.Green => ShowGreen.IsChecked==true, LogLevel.Red => ShowRed.IsChecked==true, _ => true
    };

    /// <summary>Formats elapsed duration as whole seconds for consistent UI and completion reporting.
    /// A partial final second is counted as a full displayed second so the duration agrees with
    /// the whole-second timestamps visible in the output log. No fractional seconds are shown.</summary>
    private static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is null) return "00:00:00";
        var value = elapsed.Value;
        var wholeSeconds = value == TimeSpan.Zero ? 0L : (long)Math.Ceiling(value.TotalSeconds);
        return TimeSpan.FromSeconds(wholeSeconds).ToString(@"hh\:mm\:ss");
    }

    /// <summary>Rebuilds visible read-only output while preserving severity filters and semantic colors.</summary>
    private void RefreshOutput()
    {
        if (OutputRichText is null) return;
        var document = OutputRichText.Document;
        document.Blocks.Clear();
        foreach (var entry in _events.Where(e => Visible(e.Level)))
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 5), TextAlignment = TextAlignment.Left };
            var run = new Run($"[{entry.Timestamp:HH:mm:ss}] {UserMessages.ForDisplay(entry.Message)}")
            {
                FontWeight = FontWeights.Bold,
                Foreground = entry.Level switch
                {
                    LogLevel.Blue => Brushes.DodgerBlue,
                    LogLevel.Yellow => Brushes.Goldenrod,
                    LogLevel.Green => Brushes.ForestGreen,
                    LogLevel.Red => new SolidColorBrush(Color.FromRgb(255, 82, 82)),
                    _ => Brushes.White
                }
            };
            paragraph.Inlines.Add(run);
            document.Blocks.Add(paragraph);
        }
        OutputRichText.ScrollToEnd();
    }

    // The RichTextBox intentionally keeps its selection for Ctrl+C and context-menu copying.
    // Clicking a different control or blank area dismisses the visible selection.
    // Clear history selection when the user clicks away from a grid. Preserve it for
    // command buttons (Open Selected Log / Restore Selected Archive), context menus,
    // and interactions within the grid itself.
    private void MainWindow_ClearGridSelectionOutsideRows(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
            FindAncestor<MenuItem>(source) is not null) return;

        foreach (var grid in new[] { ConfigHistoryGrid, RunHistoryGrid, RecoveryReportGrid })
        {
            if (!IsInside(source, grid)) grid.UnselectAll();
        }
    }

    private void ClearHistoryGridSelections()
    {
        ConfigHistoryGrid.UnselectAll();
        RunHistoryGrid.UnselectAll();
        RecoveryReportGrid.UnselectAll();
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (DependencyObject? node = source; node is not null; node = ParentOf(node))
            if (node is T found) return found;
        return null;
    }

    private static bool IsInside(DependencyObject source, DependencyObject target)
    {
        for (DependencyObject? node = source; node is not null; node = ParentOf(node))
            if (ReferenceEquals(node, target)) return true;
        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is System.Windows.Media.Visual || node is System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(node)
            : System.Windows.LogicalTreeHelper.GetParent(node);

    private void OutputWindow_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // RichTextBox can report a Run (TextElement), which is not a Visual.
        // Text elements in this window originate in the output document.
        if (e.OriginalSource is System.Windows.Documents.TextElement)
            return;
        if (e.OriginalSource is System.Windows.Media.Visual visual &&
            (ReferenceEquals(visual, OutputRichText) || OutputRichText.IsAncestorOf(visual)))
        {
            // A blank-area click typically originates from an output visual, not a Run.
            // Clear only for the left button; preserve the selection for right-click Copy.
            // Do not mark handled: ordinary caret placement and dragging still work.
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                ClearOutputSelection();
            return;
        }
        if (e.OriginalSource is System.Windows.Media.Media3D.Visual3D visual3D &&
            OutputRichText.IsAncestorOf(visual3D))
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                ClearOutputSelection();
            return;
        }
        ClearOutputSelection();
    }

    private void OutputRichText_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        // A context menu temporarily moves keyboard focus. Do not erase the selection
        // while Copy / Select All / Copy All Visible Output is being invoked.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!OutputRichText.IsKeyboardFocusWithin && OutputRichText.ContextMenu?.IsOpen != true)
                ClearOutputSelection();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ClearOutputSelection()
    {
        if (OutputRichText.ContextMenu?.IsOpen == true) return;
        var selection = OutputRichText.Selection;
        if (!selection.IsEmpty)
            selection.Select(selection.End, selection.End);
    }
    // Copies exactly the currently visible messages; the saved run logs remain authoritative.
    private void CopyAllOutput_Click(object sender, RoutedEventArgs e)
    {
        var lines = _events.Where(entry => Visible(entry.Level))
            .Select(entry => $"[{entry.Timestamp:HH:mm:ss}] {UserMessages.ForDisplay(entry.Message)}").ToArray();
        if (lines.Length > 0) Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }

    private void OpenLatestOutputLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string root = _config?.Application.ReportRoot ?? ReportRoot.Text.Trim();
            var latest = _runHistoryService.List(root, 1).FirstOrDefault();
            if (latest is null || !File.Exists(latest.LogPath))
            {
                MessageBox.Show(this, "No saved run log is available.", "Open Latest Log");
                return;
            }
            Process.Start(new ProcessStartInfo(latest.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the latest log: {UserMessages.Explain(ex)}", "Open Latest Log", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    /// <summary>Restores per-user presentation state without changing synchronization configuration.</summary>
    private async Task RestoreUiStateAsync()
    {
        _uiState = await _uiStateService.LoadAsync();
        MainTabs.SelectedIndex = 0;
        ZidooSection.IsExpanded = _uiState.ZidooExpanded;
        MediaMonkeySection.IsExpanded = _uiState.MediaMonkeyExpanded;
        PlaylistStorageSection.IsExpanded = _uiState.PlaylistStorageExpanded;
        AdvancedSection.IsExpanded = _uiState.AdvancedExpanded;
        ArchiveSection.IsExpanded = _uiState.ArchiveExpanded;
        ShowBlue.IsChecked = _uiState.ShowBlue;
        ShowYellow.IsChecked = _uiState.ShowYellow;
        ShowGreen.IsChecked = _uiState.ShowGreen;
        ShowRed.IsChecked = _uiState.ShowRed;
        if (_uiState.WindowWidth >= MinWidth) Width = _uiState.WindowWidth;
        if (_uiState.WindowHeight >= MinHeight) Height = _uiState.WindowHeight;
        if (_uiState.WindowLeft is double left && _uiState.WindowTop is double top &&
            left >= SystemParameters.VirtualScreenLeft && top >= SystemParameters.VirtualScreenTop &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left; Top = top;
        }
        if (_uiState.Maximized) WindowState = WindowState.Maximized;
        RefreshOutput();
    }

    /// <summary>Captures and saves presentation state when the application closes.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if(_syncCancellation is not null)
        {
            e.Cancel=true;StatusText.Text="Synchronization active; use Cancel Synchronization and let recovery finish.";return;
        }
        if (_nativeAssessmentCancellation is { IsCancellationRequested: false } cancellation)
        {
            e.Cancel = true;
            cancellation.Cancel();
            StatusText.Text = "Canceling assessment before closing. Close again when cancellation completes.";
            return;
        }
        if (_nativeAssessmentCancellation is not null)
        {
            e.Cancel = true;
            StatusText.Text = "Waiting for the assessment to stop before closing.";
            return;
        }
        var bounds = RestoreBounds;
        _uiState.SelectedTabIndex = MainTabs.SelectedIndex;
        _uiState.ZidooExpanded = ZidooSection.IsExpanded;
        _uiState.MediaMonkeyExpanded = MediaMonkeySection.IsExpanded;
        _uiState.PlaylistStorageExpanded = PlaylistStorageSection.IsExpanded;
        _uiState.AdvancedExpanded = AdvancedSection.IsExpanded;
        _uiState.ArchiveExpanded = ArchiveSection.IsExpanded;
        _uiState.ShowBlue = ShowBlue.IsChecked == true;
        _uiState.ShowYellow = ShowYellow.IsChecked == true;
        _uiState.ShowGreen = ShowGreen.IsChecked == true;
        _uiState.ShowRed = ShowRed.IsChecked == true;
        _uiState.WindowWidth = bounds.Width; _uiState.WindowHeight = bounds.Height;
        _uiState.WindowLeft = bounds.Left; _uiState.WindowTop = bounds.Top;
        _uiState.Maximized = WindowState == WindowState.Maximized;
        _uiStateService.SaveOnClosing(_uiState);
    }
}
