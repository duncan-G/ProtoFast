using System.Text.Json;
using ProtoFast.Data.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Reads a model's findings leniently: anything missing is empty, anything too long is cut.</summary>
public static class FindingsJson
{
    public static string Text(JsonElement input, string name, int max)
    {
        var text = input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : "";
        return text.Length <= max ? text : text[..max].TrimEnd();
    }

    /// <summary>Blank and repeated items are dropped.</summary>
    public static List<string> Items(JsonElement input, string name, int max) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!.Trim())
                .Select(item => item.Length <= max ? item : item[..max].TrimEnd())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(OffTheLoopLimits.ListItems)
                .ToList()
            : [];
}
