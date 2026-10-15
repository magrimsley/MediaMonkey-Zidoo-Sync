// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.1 (Major/Minor controlled exclusively by the product owner)
// Development channel: alpha.40
// Purpose: Immutable result/data contracts for native MediaMonkey playlist export.
// Remarks:
// - These contracts contain no Zidoo mutation behavior.
// - Playlist order and track order originate from the MediaMonkey database.
// ============================================================================
namespace MediaMonkeyZidooSync.Models;

/// <summary>One managed MediaMonkey playlist and its authoritative M3U filename.</summary>
public sealed record MediaMonkeyPlaylistMapEntry(
    int IDPlaylist,
    string PlaylistName,
    string M3UFile,
    int TrackCount);

/// <summary>One playlist entry that could not be resolved to a music file.</summary>
public sealed record MediaMonkeyMissingEntry(
    string Playlist,
    long SongOrder,
    string Artist,
    string Title,
    string DatabasePath,
    string ExpectedMusicPath,
    string Problem);



/// <summary>
/// One native playlist prepared entirely in memory. Lines are the exact logical
/// M3U lines that ExportAsync would write after validation succeeds.
/// </summary>
public sealed record MediaMonkeyPreparedPlaylist(
    string PlaylistName,
    string M3UFile,
    IReadOnlyList<string> Lines);

/// <summary>One difference between the native prepared mirror and the existing authoritative M3U mirror.</summary>
public sealed record MediaMonkeyExporterDifference(
    string M3UFile,
    string Problem,
    int? LineNumber,
    string? Expected,
    string? Actual);

/// <summary>Read-only equivalence result for the native exporter.</summary>
public sealed record MediaMonkeyExporterEquivalenceResult(
    bool Equivalent,
    int ExpectedPlaylistCount,
    int ExistingPlaylistCount,
    int ComparedPlaylistCount,
    int MatchingPlaylistCount,
    int PlaylistsWithDifferencesCount,
    int MissingPlaylistFileCount,
    int ContentDifferencePlaylistCount,
    int IndividualDifferenceCount,
    string ReportPath,
    MediaMonkeyPlaylistExportResult ValidationResult,
    IReadOnlyList<MediaMonkeyExporterDifference> Differences);

/// <summary>Summary returned by native MediaMonkey validation/export.</summary>
public sealed record MediaMonkeyPlaylistExportResult(
    bool ValidationOnly,
    int PlaylistCount,
    int PopulatedPlaylistCount,
    int EmptyPlaylistCount,
    int TrackCount,
    int MissingEntryCount,
    int StalePlaylistFilesRemoved,
    string SummaryReportPath,
    string PlaylistMapReportPath,
    string? MissingEntriesReportPath,
    IReadOnlyList<MediaMonkeyPlaylistMapEntry> PlaylistMap,
    IReadOnlyList<MediaMonkeyPreparedPlaylist> PreparedPlaylists);
