using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Durable, structured history of application operations; separate from presentation.</summary>
public sealed class RunHistoryService
{
    public sealed record RunRecord(string Id, DateTime Started, string Operation,
        string Result, string Elapsed, string LogPath);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Action<string>? _checkpoint;
    private readonly Action<string, Exception>? _diagnostic;

    // Optional hooks allow deterministic failure injection and diagnostic observation.
    public RunHistoryService(Action<string>? checkpoint = null,
        Action<string, Exception>? diagnostic = null)
    {
        _checkpoint = checkpoint;
        _diagnostic = diagnostic;
    }

    private void Report(string operation, Exception error)
    {
        try { if (_diagnostic is not null) _diagnostic(operation, error);
            else
            {
                System.Diagnostics.Trace.TraceError($"Run history {operation}: {error}");
                try
                {
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MediaMonkey Zidoo Sync", "Diagnostics");
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "run-history-errors.log"),
                        $"[{DateTimeOffset.Now:O}] {operation}: {error}{Environment.NewLine}");
                }
                catch (Exception diagnosticError) when (diagnosticError is IOException or UnauthorizedAccessException or NotSupportedException)
                { System.Diagnostics.Trace.TraceError($"Run-history diagnostic write failed: {diagnosticError}"); }
            } }
        catch { System.Diagnostics.Trace.TraceError($"Run-history diagnostic callback failed during {operation}"); }
    }

    private void Cleanup(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Report("run-history cleanup: " + Path.GetFileName(path), ex); }
    }

    public RunRecord Save(string root, DateTime started, string operation, string result,
        TimeSpan elapsed, IEnumerable<LogEvent> events)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Report root is required.");
        string history = Path.Combine(root, "RunHistory");
        Directory.CreateDirectory(history);
        string id = started.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N")[..8];
        string log = Path.Combine(history, id + ".log");
        string record = Path.Combine(history, id + ".json");
        string[] lines = events.Select(e => $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}] [{e.Level}] {e.Message}" +
            (string.IsNullOrEmpty(e.DiagnosticDetails) ? "" : Environment.NewLine + "[Diagnostic details]" + Environment.NewLine + e.DiagnosticDetails)).ToArray();
        // Publish the JSON index only after the log is complete. A JSON record is
        // the commit marker; incomplete logs and temporary files are never listed.
        string tempLog = log + ".tmp";
        string tempRecord = record + ".tmp";
        var item = new RunRecord(id, started, operation, result,
            $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}", log);
        bool committed = false;
        try
        {
            File.WriteAllLines(tempLog, lines, new UTF8Encoding(true));
            _checkpoint?.Invoke("log-staged");
            File.Move(tempLog, log);
            _checkpoint?.Invoke("log-published");
            File.WriteAllText(tempRecord, JsonSerializer.Serialize(item, JsonOptions), new UTF8Encoding(true));
            _checkpoint?.Invoke("record-staged");
            File.Move(tempRecord, record);
            committed = true;
            return item;
        }
        catch (Exception ex)
        {
            Report("run-history save", ex);
            throw;
        }
        finally
        {
            Cleanup(tempLog);
            Cleanup(tempRecord);
            // Only the newly generated log is eligible for rollback. Never touch
            // an earlier record. If deletion fails, the orphan is not indexed.
            if (!committed) Cleanup(log);
        }
    }

    /// <summary>Deletes only indexed application log/index pairs directly under this history folder.</summary>
    public void Delete(string root,RunRecord item) {
        var folder=Path.GetFullPath(Path.Combine(root,"RunHistory"));
        var expected=Path.Combine(folder,item.Id+".log");
        if(item.Id!=Path.GetFileName(item.Id) || item.Id.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || !string.Equals(Path.GetFullPath(item.LogPath),expected,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected entry does not belong to this history folder.");
        // Removing the index first prevents a partially removed pair from appearing as openable.
        File.Delete(Path.Combine(folder,item.Id+".json"));File.Delete(expected);
    }
    public void Prune(string root,int keep) {
        if(keep<0)throw new ArgumentOutOfRangeException(nameof(keep));
        if(keep==0)return;
        foreach(var item in List(root,int.MaxValue).Skip(keep))Delete(root,item);
    }

    public IReadOnlyList<RunRecord> List(string root, int maximum = 500)
    {
        if (string.IsNullOrWhiteSpace(root)) return Array.Empty<RunRecord>();
        string history = Path.Combine(root, "RunHistory");
        if (!Directory.Exists(history)) return Array.Empty<RunRecord>();
        var records = new List<RunRecord>();
        foreach (string file in Directory.EnumerateFiles(history, "*.json").OrderByDescending(x => x))
        {
            try
            {
                var item = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file));
                if (item is not null && item.Id == Path.GetFileNameWithoutExtension(file) &&
                    string.Equals(Path.GetFullPath(item.LogPath),
                        Path.GetFullPath(Path.Combine(history, item.Id + ".log")),
                        StringComparison.OrdinalIgnoreCase) && File.Exists(item.LogPath))
                    records.Add(item);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { Report("run-history read: " + Path.GetFileName(file), ex); }
            if (records.Count >= maximum) break;
        }
        return records.OrderByDescending(x => x.Started).ToArray();
    }
}
