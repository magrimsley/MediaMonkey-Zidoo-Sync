using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Noncritical presentation-state persistence; never controls synchronization.</summary>
public sealed class UiStateService
{
    private readonly string _statePath;
    private readonly Action<string, Exception> _report;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public UiStateService() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaMonkey Zidoo Sync", "ui-state.json"), UiStateDiagnostics.Report) { }

    // Injectable path and reporter make failure cases testable without touching real user state.
    public UiStateService(string statePath, Action<string, Exception>? report = null)
    {
        _statePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
        _report = report ?? UiStateDiagnostics.Report;
    }

    public async Task<UiState> LoadAsync()
    {
        try
        {
            if (!File.Exists(_statePath)) return new UiState();
            var json = await File.ReadAllTextAsync(_statePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UiState>(json) ?? new UiState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Report("load", ex);
            return new UiState();
        }
    }

    public async Task SaveAsync(UiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var temp = _statePath + ".tmp";
        try
        {
            EnsureDirectory();
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, JsonOptions)).ConfigureAwait(false);
            File.Move(temp, _statePath, true);
        }
        catch (Exception ex)
        {
            Report("save", ex);
            throw;
        }
        finally { CleanupTemp(temp); }
    }

    /// <summary>Bounded-lifecycle shutdown save: synchronous to finish before WPF closes the window.</summary>
    public void SaveOnClosing(UiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var temp = _statePath + ".tmp";
        try
        {
            EnsureDirectory();
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, _statePath, true);
        }
        catch (Exception ex)
        {
            Report("shutdown-save", ex);
            // Presentation state is noncritical: shutdown must not be blocked by save failure.
        }
        finally { CleanupTemp(temp); }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    private void CleanupTemp(string temp)
    {
        try { if (File.Exists(temp)) File.Delete(temp); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Report("temporary-file-cleanup", ex); }
    }

    private void Report(string operation, Exception ex)
    {
        try { _report(operation, ex); }
        catch (Exception loggingError) { Trace.TraceError($"UI-state diagnostic reporter failed: {loggingError}"); }
    }
}

/// <summary>Best-effort local diagnostics, independent of UI-state storage and application startup.</summary>
internal static class UiStateDiagnostics
{
    public static void Report(string operation, Exception exception)
    {
        Trace.TraceError($"UI state {operation}: {exception}");
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MediaMonkey Zidoo Sync", "Diagnostics");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "ui-state-errors.log"),
                $"[{DateTimeOffset.Now:O}] {operation}: {exception}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { Trace.TraceError($"UI state diagnostic write failed: {ex}"); }
    }
}
