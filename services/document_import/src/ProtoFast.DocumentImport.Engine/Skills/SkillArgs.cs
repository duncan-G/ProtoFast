using System.Text.Json;

namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>Reads skill arguments; a missing or mistyped one is the agent's error to fix.</summary>
internal static class SkillArgs
{
    public static T Required<T>(JsonElement args, string name) =>
        TryRead<T>(args, name, out var value) ? value : throw new ArgumentException($"'{name}' is required.", name);

    public static T? Optional<T>(JsonElement args, string name) where T : class =>
        TryRead<T>(args, name, out var value) ? value : null;

    public static T? OptionalValue<T>(JsonElement args, string name) where T : struct =>
        TryRead<T>(args, name, out var value) ? value : null;

    /// <summary>A document passed as a string is the text itself; anything else is its JSON.</summary>
    public static string Document(JsonElement args, string name) =>
        Property(args, name) is { } value
            ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()
            : throw new ArgumentException($"'{name}' is required.", name);

    private static bool TryRead<T>(JsonElement args, string name, out T value)
    {
        value = default!;
        if (Property(args, name) is not { } property)
        {
            return false;
        }

        try
        {
            value = property.Deserialize<T>(SkillJson.Options)!;
            return value is not null;
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"'{name}' is not a valid {typeof(T).Name}: {e.Message}", name, e);
        }
    }

    private static JsonElement? Property(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
}
