// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.1 (Major/Minor controlled by the product owner)
// Development channel: alpha.1
// Purpose: Lets the user choose among multiple positively identified Zidoo devices.
// ============================================================================
using System.Windows;
using MediaMonkeyZidooSync.Services;

namespace MediaMonkeyZidooSync;

/// <summary>Selection dialog shown only when discovery identifies multiple Zidoo devices.</summary>
public partial class ZidooDiscoveryWindow : Window
{
    public ZidooDiscoveryResult? SelectedDevice { get; private set; }

    public ZidooDiscoveryWindow(IReadOnlyList<ZidooDiscoveryResult> devices)
    {
        InitializeComponent();
        GridSelectionBehavior.Attach(DevicesGrid);
        DevicesGrid.ItemsSource = devices;
        if (devices.Count > 0) DevicesGrid.SelectedIndex = 0;
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is not ZidooDiscoveryResult selected)
        {
            MessageBox.Show(this, "Select a Zidoo device first.", "Zidoo Discovery",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedDevice = selected;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
