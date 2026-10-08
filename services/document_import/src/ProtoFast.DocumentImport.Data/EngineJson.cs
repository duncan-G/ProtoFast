using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtoFast.DocumentImport.Data;

/// <summary>How engine records are written to S3 and to jsonb columns.</summary>
internal static class EngineJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Computed properties such as StageRecord.Passed are derived, not stored.
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static byte[] SerializeToUtf8Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;

    public static T Deserialize<T>(byte[] json) => JsonSerializer.Deserialize<T>(json, Options)!;
}
