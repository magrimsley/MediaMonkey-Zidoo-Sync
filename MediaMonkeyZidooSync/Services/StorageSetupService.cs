using System.IO;
namespace MediaMonkeyZidooSync.Services;

/// <summary>Maps user-selected shared folders to the names that the device browser needs.</summary>
public static class StorageSetupService {
    public sealed record MusicLocation(string Host,string Share,string Music,string Path);
    public static MusicLocation ParseMusic(string path) {
        if(!path.StartsWith(@"\\")) throw new InvalidDataException("Choose a shared network folder. Its path starts with two backslashes, such as \\\\Server\\Share\\Music.");
        var pieces=path.TrimEnd('\\').Split('\\',StringSplitOptions.RemoveEmptyEntries);
        if(pieces.Length!=3) throw new InvalidDataException("Choose the music folder directly inside the share, for example \\\\Server\\Share\\Music.");
        return new(pieces[0],pieces[1],pieces[2],path.TrimEnd('\\'));
    }
    public static string PlaylistName(string music,string playlists) {
        var root=ParseMusic(music);
        var prefix=root.Path+"\\";
        if(!playlists.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a playlist folder inside the selected music folder.");
        var name=playlists[prefix.Length..].TrimEnd('\\');
        if(string.IsNullOrWhiteSpace(name) || name.Contains('\\'))
            throw new InvalidDataException("Choose the playlist folder inside your music folder, for example Music\\A Zidoo. Do not choose Music itself.");
        return name;
    }
}
