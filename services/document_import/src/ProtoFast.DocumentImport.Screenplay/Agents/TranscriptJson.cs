using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

/// <summary>How transcript entries are journaled; a journaled run must deserialize after a deploy.</summary>
internal static class TranscriptJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(TranscriptEntry entry) => JsonSerializer.Serialize(entry, Options);

    public static TranscriptEntry Deserialize(string json) =>
        JsonSerializer.Deserialize<TranscriptEntry>(json, Options) ?? throw new JsonException("The transcript entry is null.");
}
