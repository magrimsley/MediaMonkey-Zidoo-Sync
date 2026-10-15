// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.1 (Major/Minor controlled by the product owner)
// Development channel: alpha.4
// Purpose: Per-user presentation-state model.
// Remarks: UI state is separate from operational synchronization configuration.
// ============================================================================
namespace MediaMonkeyZidooSync.Models;

/// <summary>Presentation choices that survive application restarts.</summary>
public sealed class UiState
{
    /// <summary>Zero-based selected main-tab index.</summary>
    public int SelectedTabIndex { get; set; }
    /// <summary>Zidoo section expansion state.</summary>
    public bool ZidooExpanded { get; set; } = true;
    /// <summary>MediaMonkey section expansion state.</summary>
    public bool MediaMonkeyExpanded { get; set; } = true;
    /// <summary>Playlist Storage section expansion state.</summary>
    public bool PlaylistStorageExpanded { get; set; } = true;
    /// <summary>Advanced section expansion state.</summary>
    public bool AdvancedExpanded { get; set; }
    /// <summary>Configuration Archive section expansion state.</summary>
    public bool ArchiveExpanded { get; set; } = true;
    /// <summary>Information display-filter state.</summary>
    public bool ShowBlue { get; set; } = true;
    /// <summary>Warning/recovery display-filter state.</summary>
    public bool ShowYellow { get; set; } = true;
    /// <summary>Success/commit display-filter state.</summary>
    public bool ShowGreen { get; set; } = true;
    /// <summary>Error display-filter state.</summary>
    public bool ShowRed { get; set; } = true;
    /// <summary>Last restored window width.</summary>
    public double WindowWidth { get; set; } = 1180;
    /// <summary>Last restored window height.</summary>
    public double WindowHeight { get; set; } = 820;
    /// <summary>Last restored left coordinate.</summary>
    public double? WindowLeft { get; set; }
    /// <summary>Last restored top coordinate.</summary>
    public double? WindowTop { get; set; }
    /// <summary>Whether the window was maximized.</summary>
    public bool Maximized { get; set; }
}
