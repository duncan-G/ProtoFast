using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>How skill arguments and results cross between the agent, scripts and the engine.</summary>
public static class SkillJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static T Read<T>(JsonElement args) =>
        args.Deserialize<T>(Options) ?? throw new JsonException($"Expected a {typeof(T).Name} object.");
}
