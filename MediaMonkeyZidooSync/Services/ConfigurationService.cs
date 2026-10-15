// ============================================================================
// MediaMonkey Zidoo Sync
// Product line: 1.0 (Major/Minor controlled by the product owner)
// Development channel: alpha.19
// Purpose: Persistent configuration validation, save, archive, Last Known Good, and restore operations.
// Remarks:
// - Build and revision are compiler-generated through AssemblyVersion 1.0.*.
// - Released artifacts are immutable; any code change requires a new build identity.
// - Production PowerShell synchronization mechanics are not changed by this source-governance pass.
// ============================================================================
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Metadata for one archived configuration snapshot.</summary>
/// <param name="Folder">Archive folder name.</param>
/// <param name="Timestamp">Timestamp parsed from the archive folder.</param>
/// <param name="Reason">Reason recorded when the archive was created.</param>
/// <param name="ConfigPath">Full path to the archived JSON file.</param>
public sealed record ConfigurationArchiveEntry(string Folder, DateTime Timestamp, string Reason, string ConfigPath);

/// <summary>
/// Owns configuration persistence and recovery. Saving or restoring never contacts
/// the Zidoo; Last Known Good is marked separately after a successful synchronization.
/// </summary>
public sealed class ConfigurationService {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public string CurrentDirectory => Path.Combine(Root, "Configuration", "Current");
    public string ArchiveDirectory => Path.Combine(Root, "Configuration", "Archive");
    public string KnownGoodDirectory => Path.Combine(Root, "Configuration", "LastKnownGood");
    public string CurrentPath => Path.Combine(CurrentDirectory, "MediaMonkey-Zidoo-Config-v1.0.0.json");

    /// <summary>Initializes per-user configuration storage, unless an explicit test root is supplied.</summary>
    /// <param name="root">Optional storage root, primarily for controlled testing.</param>
    public ConfigurationService(string? root = null) {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaMonkey Zidoo Sync");
        Directory.CreateDirectory(CurrentDirectory);
        Directory.CreateDirectory(ArchiveDirectory);
        Directory.CreateDirectory(KnownGoodDirectory);
        if (root is null) ImportLegacyConfigIfPresent();
    }

    /// <summary>
    /// One-way, non-destructive migration of the legacy shared configuration.
    /// Never changes or deletes ProgramData. Existing per-user settings take precedence.
    /// Test roots never access legacy storage.
    /// </summary>
    private void ImportLegacyConfigIfPresent() {
        if (File.Exists(CurrentPath)) return;
        var legacy = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData), "MediaMonkey Zidoo Sync",
            "Configuration", "Current", "MediaMonkey-Zidoo-Config-v1.0.0.json");
        if (!File.Exists(legacy)) return;
        try {
            var json = File.ReadAllText(legacy);
            var parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                ?? throw new InvalidDataException("Legacy configuration is empty.");
            Validate(parsed);
            // Publish only complete bytes; a concurrent reader must never see an open writer.
            PublishInitialConfiguration(CurrentPath, System.Text.Encoding.UTF8.GetBytes(json));
        } catch (IOException) when (File.Exists(CurrentPath)) {
            // Another instance created the user's config; preserve it.
        } catch (Exception ex) {
            // A corrupt/inaccessible legacy config is never copied or modified.
            DiagnosticReportingService.Shared.Report("Legacy configuration migration", ex, "Warning");
        }
    }

    /// <summary>Loads the current configuration, seeding it from the packaged baseline when necessary.</summary>
    /// <param name="seedPath">Packaged baseline JSON path.</param>
    /// <returns>The deserialized configuration.</returns>
    public async Task<AppConfig> LoadAsync(string seedPath) {
        if (!File.Exists(CurrentPath)) PublishInitialConfiguration(CurrentPath, File.ReadAllBytes(seedPath));
        await using var fs = await OpenCurrentForReadAsync();
        return await JsonSerializer.DeserializeAsync<AppConfig>(fs, JsonOptions)
            ?? throw new InvalidDataException("Configuration is empty.");
    }

    private async Task<FileStream> OpenCurrentForReadAsync() {
        for (var attempt = 0; ; attempt++) {
            try { return File.OpenRead(CurrentPath); }
            catch (IOException ex) when (attempt < 5 && (ex.HResult & 0xffff) is 32 or 33) {
                // Brief Windows sharing/lock violations can outlast publication (for example scanner handles).
                // Do not retry permissions, malformed JSON, or arbitrary I/O failures.
                await Task.Delay(25 << attempt).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Stages initial bytes beside the destination, then publishes without overwriting a competing first run.</summary>
    private static void PublishInitialConfiguration(string path, byte[] bytes) {
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".initializing";
        try {
            File.WriteAllBytes(staging, bytes);
            try { File.Move(staging, path, false); }
            catch (IOException) when (File.Exists(path)) {
                // The winning instance has already published its complete configuration.
            }
        } finally {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    /// <summary>Validates, archives the previous current configuration, then atomically saves the new configuration.</summary>
    /// <param name="config">Configuration to validate and persist.</param>
    public async Task SaveAsync(AppConfig config) {
        Validate(config);
        ArchiveCurrent("PreSave");
        await WriteAsync(CurrentPath, config);
    }

    /// <summary>Copies the current configuration into a timestamped archive folder.</summary>
    /// <param name="reason">Human-readable archive reason encoded in the folder name.</param>
    /// <returns>The archive directory, or null when no current configuration exists.</returns>
    public string? ArchiveCurrent(string reason) {
        if (!File.Exists(CurrentPath)) return null;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var safeReason = string.Concat(reason.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var dir = Path.Combine(ArchiveDirectory, $"{stamp}-{safeReason}");
        Directory.CreateDirectory(dir);
        File.Copy(CurrentPath, Path.Combine(dir, Path.GetFileName(CurrentPath)), true);
        return dir;
    }

    /// <summary>Returns valid configuration archives newest-first.</summary>
    /// <returns>Read-only archive metadata collection.</returns>
    public IReadOnlyList<ConfigurationArchiveEntry> GetArchiveHistory() {
        if (!Directory.Exists(ArchiveDirectory)) return Array.Empty<ConfigurationArchiveEntry>();
        return Directory.GetDirectories(ArchiveDirectory)
            .Select(d => {
                var name = Path.GetFileName(d);
                var split = name.IndexOf('-', 19);
                var stamp = split > 0 ? name[..split] : name;
                var reason = split > 0 ? name[(split + 1)..] : "";
                DateTime.TryParseExact(stamp, "yyyyMMdd-HHmmss-fff", null,
                    System.Globalization.DateTimeStyles.None, out var dt);
                return new ConfigurationArchiveEntry(name, dt, reason,
                    Path.Combine(d, Path.GetFileName(CurrentPath)));
            })
            .Where(x => File.Exists(x.ConfigPath))
            .OrderByDescending(x => x.Timestamp)
            .ToList();
    }

    /// <summary>Marks the current configuration as Last Known Good after a successful synchronization.</summary>
    public void MarkLastKnownGood() {
        if (!File.Exists(CurrentPath)) return;
        Directory.CreateDirectory(KnownGoodDirectory);
        File.Copy(CurrentPath, Path.Combine(KnownGoodDirectory, Path.GetFileName(CurrentPath)), true);
        File.WriteAllText(Path.Combine(KnownGoodDirectory, "validated.txt"),
            $"Validated by successful synchronization: {DateTime.Now:O}{Environment.NewLine}");
    }

    /// <summary>Gets whether a Last Known Good configuration is currently available.</summary>
    public bool HasLastKnownGood =>
        File.Exists(Path.Combine(KnownGoodDirectory, Path.GetFileName(CurrentPath)));

    /// <summary>Archives the current configuration and restores the most recently validated configuration.</summary>
    public void RestoreLastKnownGood() {
        var good = Path.Combine(KnownGoodDirectory, Path.GetFileName(CurrentPath));
        if (!File.Exists(good)) throw new FileNotFoundException("No Last Known Good configuration is available.");
        ArchiveCurrent("BeforeRestoreLastKnownGood");
        File.Copy(good, CurrentPath, true);
    }

    /// <summary>Archives the current configuration and restores a selected historical snapshot.</summary>
    /// <param name="entry">Archive snapshot selected by the user.</param>
    public void RestoreArchive(ConfigurationArchiveEntry entry) {
        if (!File.Exists(entry.ConfigPath)) throw new FileNotFoundException("The selected archived configuration no longer exists.");
        ArchiveCurrent("BeforeRestoreArchive");
        File.Copy(entry.ConfigPath, CurrentPath, true);
    }

    /// <summary>Writes JSON through a temporary file and replaces the destination only after serialization completes.</summary>
    private static async Task WriteAsync(string path, AppConfig config) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var fs = File.Create(temp))
            await JsonSerializer.SerializeAsync(fs, config, JsonOptions);
        File.Move(temp, path, true);
    }

    /// <summary>Validates all required deployment values before a configuration can be saved or used.</summary>
    /// <param name="c">Configuration to validate.</param>
    /// <exception cref="InvalidDataException">Thrown when a required value or format is invalid.</exception>
    public static void Validate(AppConfig c) {
        static void Required(string value, string name) {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{name} is required.");
        }
        Required(c.Application.ReportRoot, "Report / Log Root");
        Required(c.Zidoo.IPAddress, "Zidoo IP Address");
        if (!IPAddress.TryParse(c.Zidoo.IPAddress, out _)) throw new InvalidDataException("Zidoo IP Address is not a valid IP address.");
        if (c.Zidoo.ApiPort is < 1 or > 65535) throw new InvalidDataException("Zidoo API Port must be 1-65535.");
        Required(c.Zidoo.ExpectedModel, "Expected Zidoo Model");
        Required(c.Zidoo.MacAddress, "Zidoo MAC Address");
        if (!PhysicalAddress.TryParse(c.Zidoo.MacAddress.Replace(":", "").Replace("-", ""), out _))
            throw new InvalidDataException("Zidoo MAC Address is not valid.");
        Required(c.Zidoo.BroadcastAddress, "WOL Broadcast Address");
        if (!IPAddress.TryParse(c.Zidoo.BroadcastAddress, out _)) throw new InvalidDataException("WOL Broadcast Address is not a valid IP address.");
        if (c.Zidoo.WolPort is < 1 or > 65535) throw new InvalidDataException("WOL Port must be 1-65535.");
        if (c.Zidoo.PlaylistGridColumns < 1) throw new InvalidDataException("Playlist Grid Columns must be positive.");

        Required(c.NAS.Host, "NAS Host");
        Required(c.NAS.ShareName, "NAS Share Name");
        Required(c.NAS.MusicFolderName, "NAS Music Folder Name");
        Required(c.NAS.PlaylistFolderName, "NAS Playlist Folder Name");
        Required(c.NAS.MusicRootUNC, "NAS Music Root");
        Required(c.NAS.PlaylistFolderUNC, "A Zidoo Folder");
        if (!c.NAS.MusicRootUNC.StartsWith(@"\\") || !c.NAS.PlaylistFolderUNC.StartsWith(@"\\"))
            throw new InvalidDataException("NAS paths must be UNC paths beginning with \\\\.");

        Required(c.MediaMonkey.DatabasePath, "MediaMonkey Database Path");
        Required(c.MediaMonkey.PlaylistParent, "MediaMonkey Playlist Parent");
        if (c.Timing.ZidooReadinessTimeoutSeconds < 1) throw new InvalidDataException("Zidoo readiness timeout must be positive.");
        if (c.Timing.ColdBootUiStabilizationSeconds < 0) throw new InvalidDataException("Cold boot stabilization cannot be negative.");
    }
}
