using System.Text.Json;

namespace ProtoFast.Api.Services.Directing;

/// <summary>Reads a model's JSON answer leniently: fences and prose around the object are ignored, and scalars may arrive as strings.</summary>
public static class ModelJson
{
    public static JsonDocument Parse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new ModelOutputException("The answer holds no JSON object.");
        }

        try
        {
            return JsonDocument.Parse(text[start..(end + 1)], new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException e)
        {
            throw new ModelOutputException($"The answer is not valid JSON: {e.Message}");
        }
    }

    public static string? Str(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null,
        };
    }

    public static int? Int(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.Number when value.TryGetDouble(out var real) => (int)Math.Round(real),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    public static bool Bool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    public static IEnumerable<JsonElement> Array(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return [];
        }

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray(),
            JsonValueKind.Object or JsonValueKind.String => [value],
            _ => [],
        };
    }

    /// <summary>A list of strings; a single string, or a comma-separated one, counts too.</summary>
    public static List<string> Strings(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return [];
        }

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Object ? Str(v, "name") ?? Str(v, "tag") : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .ToList(),
            JsonValueKind.String => value.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            _ => [],
        };
    }
}
