using System.IO;
namespace MediaMonkeyZidooSync.Services;

/// <summary>Offers UI suggestions without imposing folder-layout rules on the frozen process.</summary>
public static class StorageSetupService {
    public sealed record MusicLocation(string Host,string Share,string Music,string Path);
    public static MusicLocation ParseMusic(string path) {
        if(!path.StartsWith(@"\\"))throw new InvalidDataException("Choose a shared network folder whose path starts with two backslashes.");
        var pieces=path.TrimEnd('\\').Split('\\',StringSplitOptions.RemoveEmptyEntries);
        if(pieces.Length<2)throw new InvalidDataException("Choose a complete network path containing a host and share.");
        // Suggest the device's folder name only when this is a direct child of the share.
        // Keep existing manual navigation settings for other layouts; never invent them.
        return new(pieces[0],pieces[1],pieces.Length==3?pieces[2]:"",path.TrimEnd('\\'));
    }
    public static string PlaylistName(string music,string playlists) {
        var prefix=music.TrimEnd('\\')+"\\";
        if(!playlists.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return "";
        var name=playlists[prefix.Length..].TrimEnd('\\');
        return !string.IsNullOrWhiteSpace(name)&&!name.Contains('\\')?name:"";
    }
}
