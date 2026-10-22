using System.IO;
namespace MediaMonkeyZidooSync.Services;

/// <summary>Matches the frozen scripts' Get-ChildItem -LiteralPath ... -File -Filter *.m3u without -Force or -Recurse.</summary>
public static class FrozenPlaylistFiles {
    public static IEnumerable<string> Enumerate(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*.m3u",SearchOption.TopDirectoryOnly)
            .Where(file=>(file.Attributes & (FileAttributes.Hidden | FileAttributes.System))==0)
            .Select(file=>file.FullName);
}
