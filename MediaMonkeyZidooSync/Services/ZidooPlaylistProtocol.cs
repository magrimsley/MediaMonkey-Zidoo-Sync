using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MediaMonkeyZidooSync.Services;

public sealed record ZidooPlaylistRecord(string Id, string Name, int Count, int GridIndex);

/// <summary>Parses the obj/count response used by the preserved device workflow.
/// Unknown or incomplete inventories never authorize mutations.</summary>
public static class ZidooPlaylistProtocol
{
    public static IReadOnlyList<ZidooPlaylistRecord> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected the device playlist inventory array.");
        if (root.GetArrayLength() >= 500)
            throw new InvalidDataException("Inventory may be truncated at 500 entries; synchronization refused.");
        var rows = new List<ZidooPlaylistRecord>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("obj", out var obj) || obj.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Unrecognized playlist row; grid position cannot be trusted.");
            var id = Text(obj, "id");
            var name = Text(obj, "name");
            var countText = Text(entry, "count");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) ||
                !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0 ||
                !ids.Add(id) || !names.Add(name))
                throw new InvalidDataException("Missing, ambiguous or invalid playlist identity/count.");
            rows.Add(new(id, name, count, index++));
        }
        return rows.AsReadOnly();
    }

    private static string? Text(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        } : null;
}
