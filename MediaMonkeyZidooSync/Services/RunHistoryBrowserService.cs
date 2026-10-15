using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Read-only browser of existing report and log files.</summary>
public sealed class RunHistoryBrowserService
{
    public sealed record HistoryFile(string Name, string FullPath, DateTime Modified, long Bytes);

    public IReadOnlyList<HistoryFile> List(string root, int maximum = 500)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<HistoryFile>();
        // Only browse existing files. Never create, move, or modify logs.
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => new[] { ".log", ".txt", ".csv", ".json" }
                .Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Take(maximum)
            .Select(f => new HistoryFile(f.Name, f.FullName, f.LastWriteTime, f.Length))
            .ToArray();
    }
}
