// ============================================================================
// MM Zidoo Sync
// Product line: 1.1 (Major/Minor controlled by the product owner)
// Development channel: Alpha.60.18.6
// Purpose: In-application help navigation and authoritative user-facing topic content.
// Remarks:
// - Version metadata is defined by MediaMonkeyZidooSync.csproj.
// - Released artifacts are immutable; any code change requires a new build identity.
// - Production application synchronization mechanics are not changed by this source-governance pass.
// ============================================================================
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows.Navigation;

namespace MediaMonkeyZidooSync;

/// <summary>Dedicated, navigable in-application user-help window.</summary>
public partial class HelpWindow : Window
{
    private readonly string _initialTopic;
    /// <summary>Creates the Help window and optionally selects a requested initial topic.</summary>
    /// <param name="initialTopic">Stable topic key to select when the window loads.</param>
    public HelpWindow(string initialTopic="GettingStarted")
    {
        InitializeComponent();
        _initialTopic=initialTopic;
        Loaded += (_,__) => SelectInitialTopic();
    }

    /// <summary>Selects the requested topic, falling back to Getting Started.</summary>
    private void SelectInitialTopic() => SelectTopic(_initialTopic);

    /// <summary>Selects a Help topic in an already-open modeless Help window.</summary>
    public void SelectTopic(string topic)
    {
        foreach (var item in Topics.Items)
            if (item is ListBoxItem li && (string?)li.Tag == topic)
            {
                Topics.SelectedItem=li;
                li.BringIntoView();
                return;
            }
        Topics.SelectedIndex=0;
    }

    /// <summary>Displays the topic selected in the navigation list.</summary>
    private void Topics_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Topics.SelectedItem is not ListBoxItem li) return;
        ShowTopic((string?)li.Tag ?? "GettingStarted");
    }

    /// <summary>Maps a stable topic key to its title, body, and optional color legend.</summary>
    /// <param name="topic">Stable Help topic key.</param>
    private void ShowTopic(string topic)
    {
        ColorLegend.Visibility = topic == "StatusColors" ? Visibility.Visible : Visibility.Collapsed;
        ZidooDocumentationLinks.Visibility = topic == "Configuration" ? Visibility.Visible : Visibility.Collapsed;
        var helpContent=topic switch
        {
            "Synchronization" => ("Synchronization", "Validate MediaMonkey, verify the native exporter, and review the proposed changes. The application enables Run Synchronization after you approve the review.\n\n" +
                "MediaMonkey controls the managed playlists. The application reads every playlist beneath your Playlist Parent and compares it with the managed M3U files in Playlist Folder UNC. An M3U file lists tracks in playback order. Zidoo Music keeps persistent playlists that you can reopen after restarting the player.\n\n" +
                "CREATE — The application exports a new M3U file and creates the corresponding playlist in Zidoo Music.\n\n" +
                "UPDATE — On the tested Zidoo Z9X 8K, the application removes the corresponding Zidoo playlist, verifies its removal, recreates it from the updated M3U file, and verifies the result.\n\n" +
                "DELETE — When you remove a managed playlist in MediaMonkey, the application deletes the corresponding Zidoo playlist and verifies its removal before deleting the managed M3U file. It keeps recovery information if the device operation fails.\n\n" +
                "Verify Native Exporter only reads and compares files. It shows Unchanged, Create, Update, and Pending Zidoo Deletion; it does not change files or contact Zidoo. If nothing changed, the application disables Review Changes and Run Synchronization unless an interrupted transaction needs recovery.\n\n" +
                "The application leaves an already-running Zidoo on. If synchronization wakes Zidoo, it attempts to restore the original off/standby state afterward. With no managed changes, it does not contact or wake Zidoo and cannot detect edits that you made directly in Zidoo Music.\n\n" +
                "If Windows or the application stops during a run, repeat validation, verification, and review. Run Synchronization first checks the interrupted transaction, keeps a verified completed change or restores an incomplete change, and restores the recorded power state. After recovery, validate again before applying current changes. If settings, files, or playlists changed unexpectedly, the application stops and keeps the recovery details.\n\n" +
                "Each playlist change forms a separate transaction. A later failure does not undo an earlier verified transaction. Cancel Synchronization requests a controlled stop; let recovery and power restoration finish before exiting. Keep recovery backups and receipts until you resolve a failure.\n\n" +
                "The Status / Output area shows activity. Open the complete run log beneath Report / Log Root for the full record."),
            "Configuration" => ("Configuration", "Save your progress at any time with Save Configuration. You can leave fields blank and finish later. The application archives the previous settings and saves your draft without contacting Zidoo. It keeps Synchronization disabled until you save all required valid settings. You can always open Configuration and Run History / Logs. After you save a complete setup, the application opens Synchronization on future starts.\n\n" +
                "Save Configuration shows the next setting that needs attention and the exact setup log path. Choose Open Setup Log on this tab, or open the Save Configuration entry in Run History / Logs. Correct any number that you mistyped before saving. The application does not treat a saved draft as a tested configuration.\n\n" +
                "Zidoo\n\n" +
                "Turn on Zidoo and connect it to the same local network as this computer, then choose Discover Zidoo. Discovery reads the device identity; it does not change playlists, wake the device, or change network settings. Each attempt creates a Run History / Logs entry, including attempts that find no device.\n\n" +
                "IP Address identifies your Zidoo on the network. MAC Address identifies its wired Ethernet adapter and lets the application send a Wake-on-LAN message. Expected Model identifies the player you intend to manage. Discovery fills these values and API Port from the responding device. This project tests the Zidoo Z9X 8K; other models may behave differently.\n\n" +
                "API Port tells the application which network port to contact; keep 9529 unless your setup requires another value. WOL Port tells it where to send the wake message; keep 9517 unless your setup requires another value. WOL Broadcast Address reaches devices on the local network segment. Discovery calculates it from the matching network adapter and subnet mask; if it cannot identify one unambiguously, ask the person who manages your network for the broadcast address. Do not assume every network ends in .255.\n\n" +
                "Find the wired MAC address in Zidoo's ABOUT screen under Wired MAC, or check its device label or packing box. The link below opens Zidoo's illustrated MAC-address guide. You do not need developer API documentation to configure this application.\n\n" +
                "Playlist Grid Columns must match the number of playlist columns that Zidoo Music displays. The application uses that value when navigating the tested player. Music Player 8 refers to Zidoo's music application.\n\n" +
                "MediaMonkey\n\n" +
                "Database Path points to MediaMonkey's library database. For a standard MediaMonkey 5/2024 installation, Browse starts at %APPDATA%\\MediaMonkey5; select MM5.DB. Windows usually expands that path to C:\\Users\\<username>\\AppData\\Roaming\\MediaMonkey5\\MM5.DB. If you use a portable installation or another database location, choose that database instead.\n\n" +
                "Playlist Parent chooses which MediaMonkey playlists you want to send to Zidoo. In MediaMonkey's Playlists tree, create or choose a parent playlist named, for example, Zidoo Sync. Put the playlists you want to manage underneath it: Zidoo Sync > Jazz and Zidoo Sync > Rock. Enter Zidoo Sync in Playlist Parent. The application exports Jazz and Rock and any other descendants; it skips Zidoo Sync itself. Keep tracks in the child playlists. Playlists outside this parent stay outside this application's management.\n\n" +
                "Enter the parent's exact name, including capitalization, rather than a Windows folder path. Use a unique name: the application requires exactly one matching parent in the MediaMonkey database. You choose the name in MediaMonkey; this application cannot infer your intended group from your music folder.\n\n" +
                "Playlist Storage / Music\n\n" +
                "These settings describe the same shared storage in two ways: Windows uses UNC paths, and Zidoo uses names in its network browser. A UNC path begins with two backslashes. Browse can start in your Windows Music folder, but select the network location that both Windows and Zidoo can reach.\n\n" +
                "Example: Windows reaches your music at \\\\MusicServer\\Media\\Music and your playlist files at \\\\MusicServer\\Media\\Music\\Playlists. In this example enter Host = MusicServer, Share Name = Media, Music Folder Name = Music, and Playlist Folder Name = Playlists. Replace these example names with the names in your own network storage.\n\n" +
                "Host names the computer or NAS (network storage device) that shares the files. Read it immediately after the first two backslashes in the UNC path. You can also use the server's IP address if your network uses it consistently.\n\n" +
                "Share Name names the shared location on that host. Read it immediately after Host in the UNC path. It identifies the share, not the server and not the entire path.\n\n" +
                "Music Folder Name names the folder that Zidoo opens inside that share to reach your music. Playlist Folder Name names the playlist folder that Zidoo opens beneath the music folder. Enter the folder names that Zidoo's network browser displays, not a full UNC path. Keep these names consistent with your UNC paths and the folders that Zidoo can browse.\n\n" +
                "Music Root UNC gives Windows the complete network path to the music folder; the example uses \\\\MusicServer\\Media\\Music. Playlist Folder UNC gives Windows the complete path where the application writes M3U files; the example uses \\\\MusicServer\\Media\\Music\\Playlists. Select each existing location with Browse or enter its UNC path. A local path such as C:\\Users\\you\\Music does not identify a network share that Zidoo can access.\n\n" +
                "Advanced\n\n" +
                "Report / Log Root chooses the folder for synchronization reports and logs. Before you enter it, the application keeps discovery and setup logs in %LOCALAPPDATA%\\MediaMonkey Zidoo Sync\\Reports\\RunHistory. The history list also includes those earlier local logs after you choose another report folder.\n\n" +
                "Readiness Timeout sets how many seconds the application waits for Zidoo after a wake/startup attempt. Cold-Boot Stabilization adds seconds after Zidoo first responds so Music Player 8 can finish loading. Use positive readiness time and zero or more stabilization seconds. Expand or collapse sections as needed; the application remembers your layout."),
            "StatusColors" => ("Status / Output Colors", "Blue messages describe normal checks, actions, and progress.\n\n" +
                "Yellow messages flag warnings or recovery work. A warning does not necessarily mean that the entire run failed.\n\n" +
                "Green messages confirm successful work, including verified playlist transactions, recovery, and power restoration.\n\n" +
                "Red messages identify errors or recovery problems that prevent the affected work from completing successfully.\n\n" +
                "The color check boxes filter the on-screen list. Clearing a box hides that category on screen and leaves the complete log unchanged."),
            "Archive" => ("Configuration Archive", "Each save copies your previous settings into the configuration archive before replacing them. This includes incomplete setup drafts.\n\n" +
                "Select an archive and choose Restore Selected Archive to recover earlier settings. Restoration first archives your current settings. Complete and save any missing or invalid settings before synchronizing."),
            "LastKnownGood" => ("Last Known Good", "Saving configuration preserves your settings; it does not prove that they work. The application marks settings Last Known Good only after a successful synchronization verifies device changes.\n\n" +
                "Choose Restore Last Known Good to recover that proven configuration. A draft save or a run with no device changes keeps the existing Last Known Good copy."),
            "History" => ("Run History / Logs", "You can open this tab even before completing setup. The list records discovery, configuration saves, validation, exporter verification, and synchronization. Each entry shows its start time, operation, result, elapsed time, and log path.\n\n" +
                "Click an entry or its log path to highlight it, then choose Open Selected Log. Refresh Runs reloads the list. Open Setup Log on Configuration opens the latest setup-save log from the current session.\n\n" +
                "Before you set Report / Log Root, find discovery and setup logs under %LOCALAPPDATA%\\MediaMonkey Zidoo Sync\\Reports\\RunHistory. The application always keeps setup-save logs there so an incomplete report path cannot prevent access. The list continues to show these local records after you set another report folder. The complete log records details that screen filters may hide.\n\n" +
                "Earlier versions may have created reports without history entries; the application does not convert those reports into entries automatically."),
            "Troubleshooting" => ("Troubleshooting", "Cannot finish setup? Save your completed fields and return later. Read the message beside Save Configuration for the next required setting. For a failed save, choose Open Setup Log or open its entry in Run History / Logs. The message also shows the full log path. If Windows prevents the application from writing a log, the message identifies the folder it tried to use.\n\n" +
                "Cannot find Zidoo? Turn it on, confirm the network connection, and try Discover Zidoo again. Read the discovery entry in Run History / Logs. Discovery never sends a wake message.\n\n" +
                "Cannot read shared music or playlists? Open the UNC paths in Windows and browse the same host, share, and folders in Zidoo Music. Confirm that both devices can reach the storage.\n\n" +
                "Synchronization failed? Open the complete run log beneath Report / Log Root. Keep recovery files and receipts, and follow the normal validation, verification, and review steps before retrying. Screen color filters leave the log unchanged.\n\n" +
                "Settings stopped working? Restore Last Known Good or a configuration archive, review the values, and save before retrying."),
            _ => ("Getting Started", "MediaMonkey → Zidoo playlist synchronization\n\n" +
                "MM Zidoo Sync reads a group of MediaMonkey playlists and manages matching playlists in Music Player 8 on a Zidoo Z9X 8K. MediaMonkey supplies the playlist names, tracks, and track order. The application does not copy changes from Zidoo back to MediaMonkey.\n\n" +
                "Start on Configuration. Turn on Zidoo and choose Discover Zidoo. Select your MediaMonkey database, choose a Playlist Parent in MediaMonkey, and enter your shared music and playlist locations. Help → Configuration explains each field with examples. Save completed fields whenever you need to pause setup.\n\n" +
                "The application opens Configuration while setup remains incomplete. Run History / Logs stays available. After you save a complete valid configuration, the application enables Synchronization and opens it on future starts, regardless of the tab you used last.\n\n" +
                "Follow the numbered steps: Validate MediaMonkey, Verify Native Exporter, Review Changes, and Run Synchronization. Changing settings or finishing a run resets those checks. The application exports child playlists beneath your Playlist Parent and skips the parent itself.\n\n" +
                "The application compares MediaMonkey with managed M3U playlist files to identify changes. If nothing changed, it does not contact or wake Zidoo, so it cannot detect edits that you made directly in Zidoo Music.\n\n" +
                "This project tests the Zidoo Z9X 8K. Discovery may find another model, but that does not prove compatible playlist navigation or API behavior."),
        };
        TopicTitle.Text=helpContent.Item1;
        SetHelpBody(helpContent.Item2);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TopicBody.ApplyTemplate();
            if (TopicBody.Template.FindName("PART_ContentHost", TopicBody) is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToTop();
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Renders Help as selectable, read-only document paragraphs.</summary>
    private void SetHelpBody(string body)
    {
        TopicBody.Document.Blocks.Clear();
        foreach (string text in body.Replace("\r\n","\n").Split(new[] {"\n\n" +
                ""},StringSplitOptions.None))
        {
            var paragraph=new Paragraph
            {
                Margin=new Thickness(0,0,0,14),
                LineHeight=22,
                TextAlignment=TextAlignment.Left,
                FontFamily=new FontFamily("Segoe UI"),
                FontWeight=FontWeights.Normal
            };
            paragraph.Inlines.Add(new Run(text.Trim()));
            TopicBody.Document.Blocks.Add(paragraph);
        }
    }

    /// <summary>Opens the official Zidoo Network API documentation in the default browser.</summary>
    private void ZidooDocumentation_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute=true });
        e.Handled=true;
    }

    /// <summary>Closes the Help window.</summary>
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}