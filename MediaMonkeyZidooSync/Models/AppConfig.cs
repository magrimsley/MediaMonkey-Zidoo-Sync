// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.0 (Major/Minor controlled by the product owner)
// Development channel: alpha.19
// Purpose: Strongly typed model for the shared MediaMonkey/Zidoo configuration JSON.
// Remarks:
// - Build and revision are compiler-generated through AssemblyVersion 1.0.*.
// - Released artifacts are immutable; any code change requires a new build identity.
// - Production PowerShell synchronization mechanics are not changed by this source-governance pass.
// ============================================================================
namespace MediaMonkeyZidooSync.Models;

/// <summary>Root object persisted in MediaMonkey-Zidoo-Config-v1.0.0.json.</summary>
public sealed class AppConfig
{
    /// <summary>Application-level settings.</summary>
    public ApplicationConfig Application { get; set; } = new();
    /// <summary>Zidoo device and playlist-grid settings.</summary>
    public ZidooConfig Zidoo { get; set; } = new();
    /// <summary>NAS/share and music-folder settings.</summary>
    public NasConfig NAS { get; set; } = new();
    /// <summary>MediaMonkey database and managed-playlist settings.</summary>
    public MediaMonkeyConfig MediaMonkey { get; set; } = new();
    /// <summary>Operational timing settings.</summary>
    public TimingConfig Timing { get; set; } = new();
}

/// <summary>Application-level configuration values.</summary>
public sealed class ApplicationConfig
{
    /// <summary>Configuration schema/application compatibility label retained for the frozen backend.</summary>
    public string Version { get; set; } = "v1.0.0";
    /// <summary>Root directory for authoritative run output/log folders.</summary>
    public string ReportRoot { get; set; } = "";
}

/// <summary>Zidoo network, identity, WOL, and grid settings.</summary>
public sealed class ZidooConfig
{
    /// <summary>Current Zidoo IPv4/IPv6 address used by the backend.</summary>
    public string IPAddress { get; set; } = "";
    /// <summary>Zidoo HTTP API port.</summary>
    public int ApiPort { get; set; }
    /// <summary>Expected Zidoo model identity.</summary>
    public string ExpectedModel { get; set; } = "";
    /// <summary>Zidoo network adapter MAC address used for Wake-on-LAN.</summary>
    public string MacAddress { get; set; } = "";
    /// <summary>Broadcast address used for Wake-on-LAN.</summary>
    public string BroadcastAddress { get; set; } = "";
    /// <summary>UDP port used by the proven Wake-on-LAN mechanism.</summary>
    public int WolPort { get; set; }
    /// <summary>Number of playlist tiles per row used by validated grid targeting.</summary>
    public int PlaylistGridColumns { get; set; }
}

/// <summary>NAS and music/playlist path settings.</summary>
public sealed class NasConfig
{
    /// <summary>NAS host/IP used by Zidoo file-browser navigation.</summary>
    public string Host { get; set; } = "";
    /// <summary>NAS SMB share name.</summary>
    public string ShareName { get; set; } = "";
    /// <summary>Music folder name beneath the share.</summary>
    public string MusicFolderName { get; set; } = "";
    /// <summary>Playlist folder name beneath the music folder.</summary>
    public string PlaylistFolderName { get; set; } = "";
    /// <summary>Windows UNC root containing the music library.</summary>
    public string MusicRootUNC { get; set; } = "";
    /// <summary>Windows UNC directory containing authoritative exported M3U files.</summary>
    public string PlaylistFolderUNC { get; set; } = "";
}

/// <summary>MediaMonkey source-of-truth settings.</summary>
public sealed class MediaMonkeyConfig
{
    /// <summary>MediaMonkey 5 database path; environment variables may be present.</summary>
    public string DatabasePath { get; set; } = "";
    /// <summary>Parent playlist whose descendants are managed by the workflow.</summary>
    public string PlaylistParent { get; set; } = "";
}


/// <summary>Backend readiness and stabilization timing settings.</summary>
public sealed class TimingConfig
{
    /// <summary>Maximum seconds to wait for Zidoo API readiness.</summary>
    public int ZidooReadinessTimeoutSeconds { get; set; }
    /// <summary>Additional UI stabilization seconds after a cold boot.</summary>
    public int ColdBootUiStabilizationSeconds { get; set; }
}
