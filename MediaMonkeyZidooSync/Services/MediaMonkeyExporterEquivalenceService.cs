// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.1 (Major/Minor controlled exclusively by the product owner)
// Development channel: alpha.40
// Purpose: Read-only proof that the native MediaMonkey exporter prepares the
//          same logical M3U mirror as the existing authoritative playlist files.
// Remarks:
// - Calls the native validator only; it never calls ExportAsync.
// - Does not write, overwrite, rename, or delete M3U playlist files.
// - Does not contact or wake Zidoo.
// - Compares expected filenames and every logical M3U line, including order.
// - Extra or missing M3U files are differences.
// ============================================================================
using System.Globalization;
using System.IO;
using System.Text;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>
/// Compares the native exporter's in-memory M3U output with the existing
/// authoritative M3U mirror without changing either source.
/// </summary>
public sealed class MediaMonkeyExporterEquivalenceService
{
    private readonly MediaMonkeyPlaylistExporter _exporter = new();

    /// <summary>
    /// Validates MediaMonkey natively, then compares every expected M3U filename
    /// and logical line with the existing configured playlist-storage folder.
    /// </summary>
    public async Task<MediaMonkeyExporterEquivalenceResult> VerifyAsync(
        AppConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        MediaMonkeyPlaylistExportResult validation = await _exporter.ValidateAsync(config, ct);
        string folder = config.NAS.PlaylistFolderUNC;

        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException(
                $"The configured playlist-storage folder is not reachable: {folder}");

        var expected = validation.PreparedPlaylists.ToDictionary(
            x => x.M3UFile, StringComparer.OrdinalIgnoreCase);

        var existingFiles = FrozenPlaylistFiles.Enumerate(folder)
            .Select(path => new { Path = path, Name = Path.GetFileName(path) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name!, item => item.Path, StringComparer.OrdinalIgnoreCase);

        var differences = new List<MediaMonkeyExporterDifference>();
        int compared = 0;

        foreach (var item in expected.Values.OrderBy(x => x.M3UFile, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            if (!existingFiles.TryGetValue(item.M3UFile, out string? existingPath))
            {
                differences.Add(new MediaMonkeyExporterDifference(
                    item.M3UFile, "Expected playlist file is missing from the existing authoritative mirror.",
                    null, null, null));
                continue;
            }

            compared++;
            string[] actualLines = await File.ReadAllLinesAsync(existingPath, ct);
            if(actualLines.SequenceEqual(item.Lines,StringComparer.Ordinal) &&
                !(await File.ReadAllBytesAsync(existingPath,ct)).SequenceEqual(FrozenM3uContent.Bytes(item.Lines)))
                differences.Add(new MediaMonkeyExporterDifference(item.M3UFile,"M3U bytes differ from the frozen UTF-8 BOM and Windows line-ending output.",null,null,null));
            int max = Math.Max(item.Lines.Count, actualLines.Length);

            for (int i = 0; i < max; i++)
            {
                string? expectedLine = i < item.Lines.Count ? item.Lines[i] : null;
                string? actualLine = i < actualLines.Length ? actualLines[i] : null;

                if (!string.Equals(expectedLine, actualLine, StringComparison.Ordinal))
                {
                    differences.Add(new MediaMonkeyExporterDifference(
                        item.M3UFile,
                        "M3U content differs.",
                        i + 1,
                        expectedLine,
                        actualLine));
                }
            }
        }

        foreach (string extra in existingFiles.Keys
                     .Where(x => !expected.ContainsKey(x))
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            differences.Add(new MediaMonkeyExporterDifference(
                extra, "Existing authoritative mirror contains an M3U that the native exporter does not expect.",
                null, null, null));
        }

        int missingPlaylistFiles = differences
            .Where(d => d.Problem.StartsWith("Expected playlist file is missing", StringComparison.Ordinal))
            .Select(d => d.M3UFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int contentDifferencePlaylists = differences
            .Where(d => d.Problem == "M3U content differs.")
            .Select(d => d.M3UFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int unexpectedPlaylistFiles = differences
            .Where(d => d.Problem.StartsWith("Existing authoritative mirror contains", StringComparison.Ordinal))
            .Select(d => d.M3UFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int playlistsWithDifferences = differences
            .Select(d => d.M3UFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int matchingPlaylists = Math.Max(0, compared - contentDifferencePlaylists);

        string reportFolder = Path.GetDirectoryName(validation.SummaryReportPath)
            ?? throw new InvalidDataException("Validation report folder could not be determined.");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string reportPath = Path.Combine(reportFolder, $"NativeExporterEquivalence-{stamp}.txt");

        var report = new StringBuilder();
        report.AppendLine("MediaMonkey Zidoo Sync - Native Exporter Equivalence");
        report.AppendLine($"Run: {stamp}");
        report.AppendLine();
        report.AppendLine("READ-ONLY VERIFICATION");
        report.AppendLine("No M3U playlist files were changed.");
        report.AppendLine("Zidoo was not contacted.");
        report.AppendLine();
        report.AppendLine($"Configured playlist-storage folder: {folder}");
        report.AppendLine($"Native playlists expected: {expected.Count}");
        report.AppendLine($"Existing M3U files found: {existingFiles.Count}");
        report.AppendLine($"Playlist files compared: {compared}");
        report.AppendLine($"Matching existing playlist files: {matchingPlaylists}");
        report.AppendLine($"Playlists with differences: {playlistsWithDifferences}");
        report.AppendLine($"Missing expected playlist files: {missingPlaylistFiles}");
        report.AppendLine($"Existing playlists with content differences: {contentDifferencePlaylists}");
        report.AppendLine($"Unexpected existing playlist files: {unexpectedPlaylistFiles}");
        report.AppendLine($"Individual diagnostic differences: {differences.Count}");
        report.AppendLine($"Result: {(differences.Count == 0 ? "EQUIVALENT" : "PLAYLIST DIFFERENCES DETECTED")}");
        report.AppendLine();

        if (differences.Count == 0)
        {
            report.AppendLine("Every expected M3U filename and every logical M3U line matched the existing authoritative mirror.");
        }
        else
        {
            report.AppendLine("DIFFERENCES");
            foreach (var d in differences)
            {
                report.AppendLine($"File: {d.M3UFile}");
                report.AppendLine($"Problem: {d.Problem}");
                if (d.LineNumber.HasValue) report.AppendLine($"Line: {d.LineNumber.Value}");
                if (d.Expected is not null) report.AppendLine($"Expected: {d.Expected}");
                if (d.Actual is not null) report.AppendLine($"Actual: {d.Actual}");
                report.AppendLine();
            }
        }

        await File.WriteAllTextAsync(reportPath, report.ToString(), new UTF8Encoding(true), ct);

        return new MediaMonkeyExporterEquivalenceResult(
            differences.Count == 0,
            expected.Count,
            existingFiles.Count,
            compared,
            matchingPlaylists,
            playlistsWithDifferences,
            missingPlaylistFiles,
            contentDifferencePlaylists,
            differences.Count,
            reportPath,
            validation,
            differences);
    }
}
