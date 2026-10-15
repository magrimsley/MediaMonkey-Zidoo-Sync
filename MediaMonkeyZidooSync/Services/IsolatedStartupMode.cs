using System;
using System.IO;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Explicit, opt-in local smoke mode; never used for ordinary startup.</summary>
internal static class IsolatedStartupMode
{
    private static string? _root;
    internal static bool Enabled => _root is not null;

    internal static string? ResolveRoot()
    {
        var args = Environment.GetCommandLineArgs();
        if (_root is not null) return _root;
        if (args.Length == 1) return null;
        if (args.Length != 3 || args[1] != "--isolated-smoke")
            throw new ArgumentException("Unknown startup arguments.");
        var path = Path.GetFullPath(args[2]);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Smoke root must be under the Windows temporary directory.");
        if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, ".mmzs-smoke-sandbox")))
            throw new InvalidOperationException("Smoke root requires a pre-existing sandbox marker.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Smoke root cannot be a reparse point.");
        _root = path;
        return path;
    }
}
