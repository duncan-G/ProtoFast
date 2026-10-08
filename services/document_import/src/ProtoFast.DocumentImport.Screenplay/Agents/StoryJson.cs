using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

public static class StoryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("The document is null.");

    public static Stream ToStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    /// <summary>The first JSON object in a reply, so a stray sentence or code fence does not fail the stage.</summary>
    public static string ExtractObject(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        return start >= 0 && end > start ? reply[start..(end + 1)] : reply.Trim();
    }
}
