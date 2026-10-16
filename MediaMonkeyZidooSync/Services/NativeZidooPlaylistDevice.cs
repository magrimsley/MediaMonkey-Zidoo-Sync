using System.Net.Http;
using System.IO;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Native port of the preserved MP8 inventory/navigation protocol.
/// Network and delay dependencies are injectable; construction performs no I/O.</summary>
public sealed class NativeZidooPlaylistDevice : IProductionPlaylistDevice
{
    private readonly HttpClient _http;
    private readonly AppConfig _config;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
        { "Key.Up", "Key.Down", "Key.Left", "Key.Right", "Key.Ok", "Key.Select" };

    public NativeZidooPlaylistDevice(HttpClient http, AppConfig config,
        Func<int, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _config = config;
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
        if (http.BaseAddress is null || string.IsNullOrWhiteSpace(config.Zidoo.ExpectedModel) ||
            config.Zidoo.PlaylistGridColumns is < 1 or > 20)
            throw new ArgumentException("Device endpoint, expected model and grid columns are required.");
    }

    private async Task<JsonDocument> Get(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    private static void Status(JsonElement root)
    {
        if (!root.TryGetProperty("status", out var status) || !status.TryGetInt32(out int code) || code != 200)
            throw new InvalidDataException("Device command was not acknowledged with status 200.");
    }

    public async Task VerifyIdentityAsync(CancellationToken ct = default)
    {
        using var doc = await Get("/ZidooControlCenter/getModel", ct);
        Status(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("model", out var model) ||
            model.GetString() != _config.Zidoo.ExpectedModel)
            throw new InvalidDataException("Device identity differs from the configured Zidoo model.");
    }

    public async Task<IReadOnlyList<ZidooPlaylistRecord>> InventoryAsync(CancellationToken ct = default)
    {
        using var doc = await Get("/ZidooMusicControl/v2/getSongLists?sort=0&start=0&count=500", ct);
        return ZidooPlaylistProtocol.Parse(doc.RootElement.GetRawText());
    }

    private async Task Command(string path, int delay, CancellationToken ct)
    {
        using var doc = await Get(path, ct);
        Status(doc.RootElement);
        await _delay(delay, ct);
    }

    private async Task Key(string key, int delay, CancellationToken ct)
    {
        if (!AllowedKeys.Contains(key)) throw new InvalidOperationException("Remote key refused.");
        await Command("/ZidooControlCenter/RemoteControl/sendkey?key=" + Uri.EscapeDataString(key), delay, ct);
    }
    private async Task Keys(string key, int count, int delay, CancellationToken ct)
    {
        if (count is < 0 or > 500) throw new InvalidDataException("Remote navigation count is outside limits.");
        for (int i = 0; i < count; i++) await Key(key, delay, ct);
    }
    private async Task Anchor(CancellationToken ct)
    {
        await VerifyIdentityAsync(ct);
        await Command("/ZidooControlCenter/Apps/openApp?packageName=com.zidoo.audioplayer", 3000, ct);
        await VerifyIdentityAsync(ct);
        await Keys("Key.Up", 20, 250, ct);
        await Keys("Key.Down", 2, 400, ct);
        await Key("Key.Ok", 1000, ct);
        await VerifyIdentityAsync(ct);
    }

    private static bool SameInventory(IReadOnlyList<ZidooPlaylistRecord> left, IReadOnlyList<ZidooPlaylistRecord> right)
        => left.SequenceEqual(right);

    public async Task DeleteExactAsync(ZidooPlaylistRecord expected, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var before = await InventoryAsync(ct);
            var target = before.SingleOrDefault(x => x.Id == expected.Id && x.Name == expected.Name);
            if (target is null || target.Count != expected.Count)
                throw new InvalidDataException("Playlist identity/count changed before deletion.");
            await Anchor(ct);
            await Key("Key.Down", 700, ct);
            await Keys("Key.Down", target.GridIndex / _config.Zidoo.PlaylistGridColumns, 300, ct);
            await Keys("Key.Right", target.GridIndex % _config.Zidoo.PlaylistGridColumns, 300, ct);
            await VerifyIdentityAsync(ct);
            if (!SameInventory(before, await InventoryAsync(ct)))
                throw new InvalidDataException("Inventory order changed during targeting; deletion refused.");
            await Key("Key.Ok", 700, ct);
            await Key("Key.Left", 500, ct);
            await Key("Key.Ok", 500, ct);
            await Keys("Key.Down", 5, 350, ct);
            await Key("Key.Ok", 700, ct);
            await Key("Key.Select", 1200, ct);
            await _delay(1200, ct);
            var after = await InventoryAsync(ct);
            if (after.Any(x => x.Id == target.Id || x.Name == target.Name) ||
                !before.Where(x => x.Id != target.Id).Select(x => (x.Id, x.Name, x.Count))
                    .SequenceEqual(after.Select(x => (x.Id, x.Name, x.Count))))
                throw new InvalidDataException("Deletion verification failed or unrelated playlists changed; recovery required.");
        }
        finally { _gate.Release(); }
    }

    private string EncodedShare => Uri.EscapeDataString("/mnt/smb/" + _config.NAS.Host + "#" + _config.NAS.ShareName);
    private string EncodedMusic => EncodedShare + "%2F" + Uri.EscapeDataString(_config.NAS.MusicFolderName);
    private string EncodedPlaylists => EncodedMusic + "%2F" + Uri.EscapeDataString(_config.NAS.PlaylistFolderName);

    public async Task<IReadOnlyList<object>> DiagnoseDirectoriesAsync(CancellationToken ct=default)
    {
        var diagnostics=new List<object>();
        foreach(var path in new[]{EncodedShare,EncodedMusic,EncodedPlaylists})
        {
            using var document=await Get("/ZidooFileControl/getFileList?path="+path+"&type=0",ct);
            Status(document.RootElement);
            diagnostics.Add(new {EncodedPath=path,Response=document.RootElement.Clone()});
        }
        return diagnostics;
    }

    public async Task PreflightDirectoriesAsync(CancellationToken ct=default)
    {
        var share=await List(EncodedShare,false,ct);
        var music=await List(EncodedMusic,false,ct);
        if(share.Count(x=>x.Equals(_config.NAS.MusicFolderName,StringComparison.OrdinalIgnoreCase))!=1 ||
           music.Count(x=>x.Equals(_config.NAS.PlaylistFolderName,StringComparison.OrdinalIgnoreCase))!=1)
            throw new InvalidDataException("Configured import directory is missing or ambiguous.");
        _=await List(EncodedPlaylists,true,ct);
    }

    private async Task<string[]> List(string path, bool playlistsOnly, CancellationToken ct)
    {
        using var doc = await Get("/ZidooFileControl/getFileList?path=" + path + "&type=0", ct);
        Status(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("filelist", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Unrecognized device file listing.");
        var names = list.EnumerateArray().Select(x => x.GetProperty("name").GetString()
            ?? throw new InvalidDataException("Missing device filename.")).ToArray();
        // Preserve every entry and its API order. Unrelated duplicate names are valid
        // on the device and must not be deduplicated, which would shift navigation.
        return playlistsOnly ? names.Where(x => new[] { ".m3u", ".m3u8", ".evpls" }
            .Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)).ToArray() : names;
    }

    private static int UniqueIndex(string[] names,string target)
    {
        var matches=names.Select((name,index)=>(name,index)).Where(x=>x.name.Equals(target,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Expected one exact navigation target: "+target+"; found "+matches.Length);
        return matches[0].index;
    }

    public async Task ImportAsync(string filename, int expectedCount, CancellationToken ct = default)
    {
        if (filename != Path.GetFileName(filename) || !filename.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || expectedCount <= 0)
            throw new ArgumentException("A populated authoritative M3U is required.");
        await _gate.WaitAsync(ct);
        try
        {
            string name = Path.GetFileNameWithoutExtension(filename);
            var before = await InventoryAsync(ct);
            if (before.Any(x => x.Name == name)) throw new InvalidDataException("Import would duplicate an existing playlist.");
            await PreflightDirectoriesAsync(ct);
            await Anchor(ct);
            await Key("Key.Down", 450, ct); await Key("Key.Left", 450, ct);
            await Keys("Key.Down", 2, 350, ct);
            await Key("Key.Ok", 800, ct); await Key("Key.Ok", 1000, ct);
            await VerifyIdentityAsync(ct);
            await Key("Key.Down", 450, ct); await Key("Key.Ok", 900, ct);
            var share = await List(EncodedShare, false, ct);
            int music = UniqueIndex(share,_config.NAS.MusicFolderName);
            if (music < 0) throw new InvalidDataException("Configured music folder is missing on device.");
            await Keys("Key.Down", music, 300, ct); await Key("Key.Ok", 900, ct);
            var folder = await List(EncodedMusic, false, ct);
            int index = UniqueIndex(folder,_config.NAS.PlaylistFolderName);
            if (index < 0) throw new InvalidDataException("Configured playlist folder is missing on device.");
            await Keys("Key.Down", index, 300, ct); await Key("Key.Ok", 1000, ct);
            var eligible = await List(EncodedPlaylists, true, ct);
            int target = UniqueIndex(eligible,filename);
            if (target < 0) throw new InvalidDataException("Exact playlist filename is missing on device.");
            await Keys("Key.Down", target, 350, ct);
            if (!eligible.SequenceEqual(await List(EncodedPlaylists, true, ct)))
                throw new InvalidDataException("Directory order changed during import targeting.");
            await Key("Key.Ok", 1500, ct); await _delay(2500, ct);
            await Key("Key.Right", 500, ct); await Key("Key.Ok", 700, ct);
            await VerifyIdentityAsync(ct); await _delay(1500, ct);
            var after = await InventoryAsync(ct);
            var added = after.SingleOrDefault(x => x.Name == name);
            if (added is null || added.Count != expectedCount || before.Any(x => x.Id == added.Id) ||
                !before.Select(x => (x.Id, x.Name, x.Count)).SequenceEqual(after.Where(x => x.Id != added.Id).Select(x => (x.Id, x.Name, x.Count))))
                throw new InvalidDataException("Import verification failed or unrelated playlists changed; recovery required.");
        }
        finally { _gate.Release(); }
    }
}
