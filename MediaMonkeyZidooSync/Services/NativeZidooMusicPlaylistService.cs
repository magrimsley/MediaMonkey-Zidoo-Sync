// 1.1-alpha.40: read-only Zidoo Music playlist inventory and guarded mutation contracts.
// No mutating API endpoints are assumed. Actual create/update/delete requires proven UI
// transaction adapters and exact-identity verification before being enabled.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MediaMonkeyZidooSync.Services;

public sealed record ZidooMusicPlaylistIdentity(string Id, string Name);

/// <summary>Read-only inventory abstraction; alternate transports can implement it.</summary>
public interface IZidooMusicPlaylistInventory
{
    Task<IReadOnlyList<ZidooMusicPlaylistIdentity>> ListAsync(CancellationToken ct = default);
    Task<bool> ExistsExactAsync(ZidooMusicPlaylistIdentity identity, CancellationToken ct = default);
}

/// <summary>Contract for a proven, independently tested device transaction adapter.</summary>
public interface IZidooMusicPlaylistMutations
{
    Task CreateAsync(string name, string m3uPath, CancellationToken ct = default);
    Task UpdateAsync(ZidooMusicPlaylistIdentity identity, string m3uPath, CancellationToken ct = default);
    Task DeleteAsync(ZidooMusicPlaylistIdentity identity, CancellationToken ct = default);
}

/// <summary>Read-only implementation of the proven Zidoo Music inventory endpoint.</summary>
public sealed class NativeZidooMusicPlaylistService : IZidooMusicPlaylistInventory
{
    private readonly HttpClient _client;
    private readonly Uri _baseUri;
    public NativeZidooMusicPlaylistService(HttpClient client, Uri baseUri)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        if (_baseUri.Scheme != Uri.UriSchemeHttp && _baseUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("HTTP(S) URI required", nameof(baseUri));
    }

    public async Task<IReadOnlyList<ZidooMusicPlaylistIdentity>> ListAsync(CancellationToken ct = default)
    {
        var endpoint = new Uri(_baseUri, "/ZidooMusicControl/v2/getSongLists?sort=0&start=0&count=500");
        using var response = await _client.GetAsync(endpoint, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        // Fail closed if the firmware response shape is not recognized. No guessed identities.
        if (!TryFindArray(json.RootElement, out var list))
            throw new InvalidOperationException("Unrecognized Zidoo playlist inventory response; no action permitted.");
        var results = new List<ZidooMusicPlaylistIdentity>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Unexpected playlist entry.");
            string? id = GetString(item, "id");
            string? name = GetString(item, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Playlist inventory has missing identity fields.");
            results.Add(new ZidooMusicPlaylistIdentity(id, name));
        }
        if (results.GroupBy(x => x.Id, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Duplicate Zidoo playlist identity.");
        return results.AsReadOnly();
    }

    public async Task<bool> ExistsExactAsync(ZidooMusicPlaylistIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return (await ListAsync(ct)).Any(x => string.Equals(x.Id, identity.Id, StringComparison.Ordinal)
            && string.Equals(x.Name, identity.Name, StringComparison.Ordinal));
    }

    private static string? GetString(JsonElement obj, string key)
    {
        foreach (var property in obj.EnumerateObject())
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null
                };
        return null;
    }

    private static bool TryFindArray(JsonElement element, out JsonElement array)
    {
        if (element.ValueKind == JsonValueKind.Array) { array = element; return true; }
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var prop in element.EnumerateObject())
                if (string.Equals(prop.Name, "data", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, "list", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, "songLists", StringComparison.OrdinalIgnoreCase))
                    if (TryFindArray(prop.Value, out array)) return true;
        array = default;
        return false;
    }
}
