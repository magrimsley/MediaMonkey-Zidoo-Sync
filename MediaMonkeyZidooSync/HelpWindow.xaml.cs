// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.1 (Major/Minor controlled by the product owner)
// Development channel: alpha.18
// Purpose: In-application help navigation and authoritative user-facing topic content.
// Remarks:
// - Build and revision are compiler-generated through AssemblyVersion 1.0.*.
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
            "Synchronization" => ("Synchronization",
                "First validate MediaMonkey, verify the native exporter, and review the proposed changes. Only then is Run Synchronization enabled. Native Assessment is an optional read-only diagnostic.\n\nRun Synchronization compares the playlists beneath the configured MediaMonkey Playlist Parent with the corresponding managed M3U playlist files in the configured playlist-storage folder. MediaMonkey is authoritative: playlist creation, track changes, track-order changes, and playlist removal in MediaMonkey determine what the application synchronizes.\n\n" +
                "CREATE — A managed playlist exists in MediaMonkey and needs to be created in Zidoo Music. The application exports the M3U file and creates the corresponding persistent playlist in Zidoo Music. A persistent playlist is a playlist saved by Zidoo Music and available again after leaving the playlist screen or restarting the player.\n\n" +
                "UPDATE — A managed MediaMonkey playlist has changed. The currently tested Zidoo Z9X 8K process removes the corresponding existing Zidoo Music playlist, verifies that the intended playlist is absent, recreates it from the updated M3U file, and verifies the recreated playlist.\n\n" +
                "DELETE — A previously managed playlist has been removed from MediaMonkey. The application first deletes the corresponding persistent playlist from Zidoo Music and verifies that the intended playlist is gone. Only after that verification does it remove the corresponding managed M3U file from playlist storage. Keeping the M3U file until the Zidoo device deletion is verified preserves information needed for recovery if the Zidoo device operation fails.\n\n" +
                "Power handling — If the Zidoo device was already on when synchronization began, the application leaves it on. If synchronization wakes a Zidoo that was off, the process is designed to restore the original off/standby state after the run. If there are no managed playlist changes, the application does not wake or contact the Zidoo device.\n\n" +
                "Verify Native Exporter compares the expected MediaMonkey playlists with existing M3U files and displays a read-only transaction plan: Unchanged, Create, Update, and Pending Zidoo Deletion. This verification does not change playlist files or contact Zidoo. Pending Zidoo Deletion does not delete any file; removal must wait for verified deletion on the Zidoo device.\n\n" +
                "Each playlist change is handled as a separate transaction. A failure involving one playlist does not undo an earlier independent playlist transaction that was already completed and verified.\n\n" +
                "Review and approve the proposed change summary before synchronization. Cancel Synchronization requests a controlled stop; let recovery and power restoration finish before exiting. If recovery is required, keep all backups and receipts and do not remove the recovery gate to force another run.\n\nThe Status / Output area shows run activity. The complete run log stored beneath Report / Log Root is the authoritative record of the synchronization."),
            "Configuration" => ("Configuration",
                "Use the Configuration tab to tell the application where MediaMonkey stores its information, where playlist files belong, and how to communicate with Zidoo and Music Player 8. You can expand or collapse each section. The application remembers which sections you leave open or closed.\n\n" +
                "Zidoo\n\n" +
                "Before using Discover Zidoo, make sure your Zidoo device is powered on and connected to the same local network as this computer. Discover Zidoo only reads information from devices on the local network. It does not change network settings or settings on Zidoo.\n\n" +
                "Discover Zidoo — Looks for a powered-on Zidoo device on the same local network as this computer. The application asks possible devices for basic identification information. The responding Zidoo device provides information such as its model, Internet Protocol (IP) address, Media Access Control (MAC) address, and software version. Discovery only reads this information; it does not change settings on Zidoo.\n\n" +
                "Internet Protocol (IP) Address — Acts as a unique digital mailing address for the Zidoo device, allowing the application and Zidoo to send and receive information across the local network. Discover Zidoo can fill in this address automatically.\n\n" +
                "API Port — A port is similar to a numbered doorway used by software on a network device. The application uses port 9529 to communicate with Zidoo. Zidoo specifies 9529 for this communication. Most users should not change this value.\n\n" +
                "Expected Model — Identifies the Zidoo device model that the application has been developed and tested with. The tested hardware is the Zidoo Z9X 8K device. The application does not presume that other Zidoo models behave exactly the same way.\n\n" +
                "Media Access Control (MAC) Address — A unique hardware address assigned to the wired Ethernet connection on the Zidoo device. The application uses this address when it needs to wake Zidoo over the local network. Discover Zidoo can fill in the address automatically. You can also find the wired Ethernet address under ABOUT → Wired MAC on Zidoo, on the label attached to the Zidoo device, or on the packing box.\n\n" +
                "Broadcast Address — Identifies all devices on the local network segment when the application sends a Wake-on-LAN message. Wake-on-LAN allows the application to turn on a compatible Zidoo over its wired network connection without requiring someone to press the power button.\n\n" +
                "Wake-on-LAN (WOL) Port — The numbered network doorway used for the Wake-on-LAN message. Zidoo specifies port 9517 for this purpose. Most users should not change this value.\n\n" +
                "Music Player 8 — The Zidoo music application with which the application integrates. Playlist synchronization creates, updates, and deletes the managed playlists used by Music Player 8 on the tested Zidoo Z9X 8K.\n\n" +
                "MediaMonkey\n\n" +
                "Database Path — Tells the application where MediaMonkey stores its library and playlist information on this computer. The normal MediaMonkey database is named MM5.DB.\n\n" +
                "Playlist Parent — The MediaMonkey playlist folder that defines which playlists the application manages. The application manages playlists beneath this parent. The parent itself is only a container and is not exported as a playlist.\n\n" +
                "Playlist Storage / Music\n\n" +
                "Music Root — Identifies the main folder that contains the music files referenced by the managed playlists.\n\n" +
                "Playlist Folder — Identifies the folder where the application writes the M3U playlist files created from MediaMonkey. An M3U file is a playlist file that lists the music tracks and their order.\n\n" +
                "Advanced\n\n" +
                "Report / Log Root — Identifies the main folder where the application stores synchronization run information. The complete run log records what the application checked, changed, verified, recovered, or could not complete.\n\n" +
                "Readiness Timeout — Sets how long the application waits for Zidoo to become available after a wake or startup attempt before it reports that Zidoo did not become ready.\n\n" +
                "Cold-Boot Stabilization — Adds a short waiting period after Zidoo first becomes reachable following a cold start. This gives Music Player 8 and the Zidoo device interface time to finish loading before playlist synchronization begins."),
            "StatusColors" => ("Status / Output Colors",
                "Blue — Information. These messages describe normal checks and actions, such as reading configuration, comparing playlists, contacting a device, or reporting progress.\n\n" +
                "Yellow — Warning / Recovery. These messages identify an unusual condition, a warning that deserves attention, or recovery work performed after an operation did not proceed normally. A yellow message does not automatically mean the entire synchronization failed.\n\n" +
                "Green — Success / Commit. These messages identify work that completed successfully, including a verified playlist transaction, a successful recovery, or restoration of the Zidoo device's original power state.\n\n" +
                "Red — Error. These messages identify an operation that could not complete normally, a failed recovery, or another condition that prevents the affected work from being considered successful.\n\n" +
                "The Blue, Yellow, Green, and Red check boxes on the Synchronization tab are display filters only. Clearing a check box hides that category from the on-screen list; it does not remove those events from the complete run log."),
            "Archive" => ("Configuration Archive",
                "Saving configuration archives the current configuration before replacing it. The archive provides a reversible history of configuration changes.\n\nRestoring an archive first archives the current configuration, then restores the selected prior copy."),
            "LastKnownGood" => ("Last Known Good",
                "Saved does not mean Last Known Good. A configuration becomes Last Known Good only after a successful synchronization validates it in actual operation.\n\nRestore Last Known Good is intended to recover the most recently proven configuration."),
            "History" => ("Run History / Logs",
                "The complete synchronization log stored beneath the configured Report / Log Root is the authoritative record of what occurred during a run. The Run History / Logs tab lists recorded application runs with their start time, operation, result, and elapsed time. Select a run and choose Open Selected Log to read its full application event log. Refresh Runs updates the list. New entries are recorded for synchronization attempts, MediaMonkey validation, and native exporter verification. Earlier reports created before this feature are not automatically converted into run-history entries."),
            "Troubleshooting" => ("Troubleshooting",
                "Configuration validation: verify required values, IP and MAC addresses, ports, configured paths, and timing values.\n\nZidoo connectivity: verify the configured device/network values. When synchronization needs the Zidoo device and it was originally off, the application uses Wake-on-LAN to start it and is designed to restore the original off/standby state after the run.\n\nPlaylist storage access: verify the configured music and playlist locations are reachable. If network paths are configured, verify the network host/share and paths are accessible.\n\nSynchronization failure: use the complete synchronization log stored beneath Report / Log Root to determine which operation failed and what recovery action occurred. UI color filters do not alter that log.\n\nConfiguration regression: restore Last Known Good or a prior Configuration Archive."),
            _ => ("Getting Started",
                "MediaMonkey Zidoo Sync is an application that keeps a defined set of MediaMonkey playlists synchronized with Music Player 8 on a Zidoo Z9X 8K device. This Help guide refers to MediaMonkey Zidoo Sync as the application after this introduction.\n\n" +
                "MediaMonkey is the source of truth. The application reads playlists located beneath the configured MediaMonkey Playlist Parent. The Playlist Parent itself is only a MediaMonkey folder used to define which playlists are managed; the parent is not exported as a playlist.\n\n" +
                "For each managed MediaMonkey playlist, the application creates or updates an M3U playlist file in the configured playlist-storage folder. An M3U file is a playlist file containing references to the music tracks and their order. The application compares MediaMonkey with these managed M3U files to determine whether a playlist was created, changed, or removed.\n\n" +
                "When a managed playlist has changed, the application communicates with Zidoo Music and performs the required create, update, or delete operation. Changes made directly to playlists in Zidoo Music are not copied back into MediaMonkey.\n\n" +
                "When MediaMonkey and the configured playlist-storage folder show no managed playlist changes, the application does not wake or contact Zidoo. Because the application does not contact Zidoo during that run, it cannot detect changes made directly in Zidoo Music.\n\n" +
                "Before the first synchronization, open Configuration. Click the Discover Zidoo button, which can locate a powered-on Zidoo device on the same local network and populate its Internet Protocol (IP) address, wired Ethernet Media Access Control (MAC) address, model, and API port. Confirm the MediaMonkey and playlist-storage settings, then save the configuration.\n\n" +
                "Follow the numbered steps: Validate MediaMonkey, Verify Native Exporter, Review Changes, then Run Synchronization. Native Assessment is optional. Changing configuration or finishing a synchronization requires starting the checks again. The Status / Output area records what the application is doing and whether each operation succeeds, produces a warning, performs recovery work, or fails.\n\n" +
                "Hardware scope: this application has been developed and tested with the Zidoo Z9X 8K device. Other Zidoo models may be discovered, but this project does not presume that their API behavior or Zidoo Music screen navigation is identical.")

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
        foreach (string text in body.Replace("\r\n","\n").Split(new[] {"\n\n"},StringSplitOptions.None))
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