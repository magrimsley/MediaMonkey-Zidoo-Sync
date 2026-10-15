using System;
using System.IO;
using System.Text.Json;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Best-effort, thread-safe append-only diagnostics independent of Run History.</summary>
public sealed class DiagnosticReportingService
{
    public static DiagnosticReportingService Shared { get; } = new();
    private readonly object _sync = new();
    private readonly string _path = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "MediaMonkeyZidooSync", "Diagnostics", "exceptions.jsonl");
    public string LogPath => _path;

    public void Report(string operation, Exception exception, string severity = "Error")
    {
        ArgumentNullException.ThrowIfNull(exception);
        Append(operation, severity, exception.ToString());
    }

    public void Record(string operation, string severity, string detail) => Append(operation, severity, detail);

    private void Append(string operation, string severity, string detail)
    {
        try
        {
            var entry = JsonSerializer.Serialize(new {
                TimestampUtc = DateTimeOffset.UtcNow, Severity = severity,
                Operation = operation, ProcessId = Environment.ProcessId,
                Detail = detail
            });
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, entry + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            // Diagnostics failure must not cause recursion or hide the original exception.
            System.Diagnostics.Trace.WriteLine("Diagnostic log unavailable: " + ex);
        }
    }
}
