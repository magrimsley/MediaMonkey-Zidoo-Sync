// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.1 (Major/Minor controlled exclusively by the product owner)
// Development channel: alpha.40
// Purpose: Native, read-only MediaMonkey database reader and authoritative M3U exporter.
// Remarks:
// - Reimplements only the proven responsibility of
//   Export-MediaMonkey-Playlists-To-ZidooM3U-v1.0.0.ps1.
// - Uses Microsoft.Data.Sqlite directly; it never launches sqlite3.exe, PowerShell,
//   BAT files, or another shell process.
// - Reads MM5.DB in read-only mode and never modifies MediaMonkey.
// - Validation completes for every managed entry before any M3U is written/deleted.
// - The configured Playlist Parent is a container and is never exported.
// - Descendants are recursive; playlist hierarchy is flattened into M3U filenames.
// - PlaylistSongs.SongOrder and IDPlaylistSong preserve deterministic track order.
// ============================================================================
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>
/// Reads managed playlists from MediaMonkey and validates/exports the authoritative
/// M3U mirror used by later synchronization stages.
/// </summary>
public sealed class MediaMonkeyPlaylistExporter
{
    private sealed record PlaylistRow(int Id, string Name, int ParentId);
    private sealed record EntryRow(int PlaylistId, long PlaylistSongId, long SongId, long SongOrder,
        string SongPath, string Artist, string Title, long SongLength);
    private sealed record PreparedPlaylist(PlaylistRow Playlist, string TargetPath, string[] Lines,
        MediaMonkeyPlaylistMapEntry MapEntry);

    /// <summary>
    /// Reads MediaMonkey and validates every managed playlist entry without changing M3U files.
    /// Reports are written beneath the configured report root.
    /// </summary>
    public Task<MediaMonkeyPlaylistExportResult> ValidateAsync(AppConfig config, CancellationToken ct = default) =>
        ExecuteAsync(config, validationOnly: true, ct);

    /// <summary>
    /// Reads MediaMonkey, validates all managed entries, then atomically refreshes the M3U mirror.
    /// No M3U file is changed when validation fails.
    /// </summary>
    public Task<MediaMonkeyPlaylistExportResult> ExportAsync(AppConfig config, CancellationToken ct = default) =>
        ExecuteAsync(config, validationOnly: false, ct);

    private static async Task<MediaMonkeyPlaylistExportResult> ExecuteAsync(
        AppConfig config, bool validationOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        string databasePath = Environment.ExpandEnvironmentVariables(config.MediaMonkey.DatabasePath);
        string parentName = config.MediaMonkey.PlaylistParent;
        string musicRoot = config.NAS.MusicRootUNC;
        string outputFolder = config.NAS.PlaylistFolderUNC;
        string reportRoot = config.Application.ReportRoot;

        if (!File.Exists(databasePath))
            throw new FileNotFoundException($"MediaMonkey database not found: {databasePath}", databasePath);
        if (!Directory.Exists(musicRoot))
            throw new DirectoryNotFoundException($"Music root is not reachable: {musicRoot}");
        if (string.IsNullOrWhiteSpace(parentName))
            throw new InvalidDataException("MediaMonkey Playlist Parent is required.");
        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new InvalidDataException("Playlist Folder is required.");
        if (string.IsNullOrWhiteSpace(reportRoot))
            throw new InvalidDataException("Report / Log Root is required.");

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string reportFolder = CreateUniqueRunFolder(reportRoot, stamp);
        string summaryPath = Path.Combine(reportFolder, $"MediaMonkeyPlaylistExport-{stamp}.txt");
        string missingPath = Path.Combine(reportFolder, $"MediaMonkeyPlaylistExport-Missing-{stamp}.csv");
        string mapPath = Path.Combine(reportFolder, $"MediaMonkeyPlaylistExport-PlaylistMap-{stamp}.csv");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        var playlists = await ReadPlaylistsAsync(connection, ct);
        var parentMatches = playlists.Where(x => string.Equals(x.Name, parentName, StringComparison.Ordinal)).ToList();
        if (parentMatches.Count != 1)
            throw new InvalidDataException($"Expected exactly one '{parentName}' container; found {parentMatches.Count}.");

        var managed = GetDescendants(playlists, parentMatches[0].Id)
            .OrderBy(x => x.Id)
            .ToList();
        var entries = await ReadEntriesAsync(connection, ct);
        var entriesByPlaylist = entries
            .GroupBy(x => x.PlaylistId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EntryRow>)g
                .OrderBy(x => x.SongOrder).ThenBy(x => x.PlaylistSongId).ToList());

        var generatedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<MediaMonkeyMissingEntry>();
        var prepared = new List<PreparedPlaylist>();
        int populated = 0, empty = 0, totalEntries = 0;

        foreach (var playlist in managed)
        {
            ct.ThrowIfCancellationRequested();

            string fileName = GetSafeFileName(playlist.Name) + ".m3u";
            if (!generatedNames.Add(fileName))
                throw new InvalidDataException($"Two MediaMonkey playlists resolve to the same Windows filename: {fileName}");

            IReadOnlyList<EntryRow> playlistEntries = entriesByPlaylist.TryGetValue(playlist.Id, out var found)
                ? found : Array.Empty<EntryRow>();

            if (playlistEntries.Count > 0) populated++; else empty++;
            totalEntries += playlistEntries.Count;

            var mapEntry = new MediaMonkeyPlaylistMapEntry(
                playlist.Id, playlist.Name, fileName, playlistEntries.Count);

            var lines = new List<string>
            {
                "#EXTM3U",
                "#EXTENC: UTF-8",
                $"#PLAYLIST:{playlist.Name}"
            };

            foreach (var entry in playlistEntries)
            {
                const string marker = @"\Music\";
                int markerIndex = entry.SongPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                {
                    missing.Add(new MediaMonkeyMissingEntry(
                        playlist.Name, entry.SongOrder, entry.Artist, entry.Title,
                        entry.SongPath, "", @"Database path does not contain \Music\"));
                    continue;
                }

                string relativeWindows = entry.SongPath[(markerIndex + marker.Length)..];
                string expectedPath = Path.Combine(musicRoot, relativeWindows);
                if (!File.Exists(expectedPath))
                {
                    missing.Add(new MediaMonkeyMissingEntry(
                        playlist.Name, entry.SongOrder, entry.Artist, entry.Title,
                        entry.SongPath, expectedPath, "Referenced music file not found"));
                    continue;
                }

                long durationSeconds = entry.SongLength > 0 ? (long)Math.Floor(entry.SongLength / 1000d) : 0;
                string artist = EscapeM3uDisplayText(entry.Artist);
                string title = EscapeM3uDisplayText(entry.Title);
                string display = !string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title)
                    ? $"{artist} - {title}"
                    : !string.IsNullOrEmpty(title)
                        ? title
                        : Path.GetFileNameWithoutExtension(relativeWindows);

                string zidooRelative = "../" + relativeWindows.Replace('\\', '/');
                lines.Add($"#EXTINF:{durationSeconds},{display}");
                lines.Add(zidooRelative);
            }

            prepared.Add(new PreparedPlaylist(
                playlist,
                Path.Combine(outputFolder, fileName),
                lines.ToArray(),
                mapEntry));
        }

        await WritePlaylistMapAsync(mapPath, prepared.Select(x => x.MapEntry), ct);

        if (missing.Count > 0)
        {
            await WriteMissingAsync(missingPath, missing, ct);
            string failedSummary = BuildSummary(
                "VALIDATION FAILED", databasePath, parentName, outputFolder, musicRoot,
                managed.Count, populated, empty, totalEntries, missing.Count, 0,
                validationOnly, summaryPath, mapPath, missingPath);
            await File.WriteAllTextAsync(summaryPath, failedSummary, new UTF8Encoding(true), ct);
            throw new InvalidDataException(
                $"MediaMonkey playlist validation failed for {missing.Count} entr{(missing.Count == 1 ? "y" : "ies")}. No M3U files were written or deleted. Report: {missingPath}");
        }

        int staleRemoved = 0;
        if (!validationOnly)
        {
            Directory.CreateDirectory(outputFolder);

            // Validation is now complete for every playlist. Only now may the mirror change.
            var utf8Bom = new UTF8Encoding(true);
            foreach (var item in prepared)
            {
                ct.ThrowIfCancellationRequested();
                string temp = item.TargetPath + ".tmp";
                await File.WriteAllLinesAsync(temp, item.Lines, utf8Bom, ct);
                File.Move(temp, item.TargetPath, true);
            }

            foreach (var file in FrozenPlaylistFiles.Enumerate(outputFolder))
            {
                if (!generatedNames.Contains(Path.GetFileName(file)))
                {
                    File.Delete(file);
                    staleRemoved++;
                }
            }
        }

        string status = validationOnly ? "VALIDATION PASSED" : "SUCCESS";
        string summary = BuildSummary(
            status, databasePath, parentName, outputFolder, musicRoot,
            managed.Count, populated, empty, totalEntries, 0, staleRemoved,
            validationOnly, summaryPath, mapPath, null);
        await File.WriteAllTextAsync(summaryPath, summary, new UTF8Encoding(true), ct);

        return new MediaMonkeyPlaylistExportResult(
            validationOnly, managed.Count, populated, empty, totalEntries, 0, staleRemoved,
            summaryPath, mapPath, null,
            prepared.Select(x => x.MapEntry).ToList(),
            prepared.Select(x => new MediaMonkeyPreparedPlaylist(
                x.Playlist.Name,
                Path.GetFileName(x.TargetPath),
                (IReadOnlyList<string>)x.Lines)).ToList());
    }

    private static async Task<List<PlaylistRow>> ReadPlaylistsAsync(SqliteConnection connection, CancellationToken ct)
    {
        const string sql = """
            SELECT IDPlaylist, PlaylistName, ParentPlaylist
            FROM Playlists
            ORDER BY IDPlaylist;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<PlaylistRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new PlaylistRow(
                Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    private static async Task<List<EntryRow>> ReadEntriesAsync(SqliteConnection connection, CancellationToken ct)
    {
        const string sql = """
            SELECT
              ps.IDPlaylist,
              ps.IDPlaylistSong,
              ps.IDSong,
              ps.SongOrder,
              s.SongPath,
              s.Artist,
              s.SongTitle,
              s.SongLength
            FROM PlaylistSongs ps
            JOIN Songs s ON s.ID = ps.IDSong
            ORDER BY ps.IDPlaylist, ps.SongOrder, ps.IDPlaylistSong;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<EntryRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new EntryRow(
                Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6),
                reader.IsDBNull(7) ? 0 : Convert.ToInt64(reader.GetValue(7), CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    private static IEnumerable<PlaylistRow> GetDescendants(IReadOnlyList<PlaylistRow> playlists, int parentId)
    {
        var byParent = playlists.GroupBy(x => x.ParentId).ToDictionary(g => g.Key, g => g.ToList());
        var queue = new Queue<int>();
        queue.Enqueue(parentId);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!byParent.TryGetValue(current, out var children)) continue;
            foreach (var child in children)
            {
                yield return child;
                queue.Enqueue(child.Id);
            }
        }
    }

    private static string GetSafeFileName(string name)
    {
        string safe = name.Trim();
        foreach (char ch in Path.GetInvalidFileNameChars())
            safe = safe.Replace(ch, '_');
        safe = safe.TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(safe))
            throw new InvalidDataException($"A playlist has an empty/invalid filename after sanitizing: '{name}'");
        return safe;
    }

    private static string EscapeM3uDisplayText(string? text) =>
        string.IsNullOrEmpty(text)
            ? ""
            : text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string CreateUniqueRunFolder(string reportRoot, string stamp)
    {
        Directory.CreateDirectory(reportRoot);
        string folder = Path.Combine(reportRoot, stamp);
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            return folder;
        }

        for (int i = 1; i < 1000; i++)
        {
            string candidate = Path.Combine(reportRoot, $"{stamp}-{i:000}");
            if (!Directory.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
        }
        throw new IOException("Unable to allocate a unique report folder.");
    }

    private static async Task WritePlaylistMapAsync(
        string path, IEnumerable<MediaMonkeyPlaylistMapEntry> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"IDPlaylist\",\"PlaylistName\",\"M3UFile\",\"TrackCount\"");
        foreach (var r in rows)
            sb.AppendLine($"{Csv(r.IDPlaylist)},{Csv(r.PlaylistName)},{Csv(r.M3UFile)},{Csv(r.TrackCount)}");
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), ct);
    }

    private static async Task WriteMissingAsync(
        string path, IEnumerable<MediaMonkeyMissingEntry> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"Playlist\",\"SongOrder\",\"Artist\",\"Title\",\"DatabasePath\",\"ExpectedMusicPath\",\"Problem\"");
        foreach (var r in rows)
            sb.AppendLine($"{Csv(r.Playlist)},{Csv(r.SongOrder)},{Csv(r.Artist)},{Csv(r.Title)},{Csv(r.DatabasePath)},{Csv(r.ExpectedMusicPath)},{Csv(r.Problem)}");
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), ct);
    }

    private static string Csv(object? value)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static string BuildSummary(
        string status, string databasePath, string parentName, string outputFolder, string musicRoot,
        int playlistCount, int populated, int empty, int trackCount, int missingCount, int staleRemoved,
        bool validationOnly, string summaryPath, string mapPath, string? missingPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"MediaMonkey Native Playlist Export - {status}");
        sb.AppendLine();
        sb.AppendLine($"MediaMonkey DB: {databasePath}");
        sb.AppendLine($"Parent container excluded: {parentName}");
        sb.AppendLine($"Playlist folder: {outputFolder}");
        sb.AppendLine($"Music root: {musicRoot}");
        sb.AppendLine();
        sb.AppendLine($"Playlists discovered: {playlistCount}");
        sb.AppendLine($"Populated playlists: {populated}");
        sb.AppendLine($"Empty playlists: {empty}");
        sb.AppendLine($"Playlist entries checked: {trackCount}");
        sb.AppendLine($"Missing/unresolved entries: {missingCount}");
        if (!validationOnly) sb.AppendLine($"Stale M3Us removed: {staleRemoved}");
        sb.AppendLine();
        if (validationOnly)
            sb.AppendLine("VALIDATION ONLY: NO M3U FILES WERE WRITTEN OR DELETED.");
        else
        {
            sb.AppendLine("Playlist hierarchy: FLAT");
            sb.AppendLine("Song ordering source: PlaylistSongs.SongOrder, then IDPlaylistSong");
            sb.AppendLine("MediaMonkey database modified: NO");
            sb.AppendLine("Music files modified/copied: NO");
        }
        sb.AppendLine();
        sb.AppendLine("Reports:");
        sb.AppendLine(summaryPath);
        sb.AppendLine(mapPath);
        if (!string.IsNullOrWhiteSpace(missingPath)) sb.AppendLine(missingPath);
        return sb.ToString();
    }
}
