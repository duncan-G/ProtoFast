using System.Text.Json;
using ProtoFast.DocumentImport.Worker.HistoricalStories;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.HistoricalStories;

public sealed class ResearchToolsTests
{
    // DeepSeek refuses a function whose object schema has no required array.
    [Fact]
    public void Every_object_in_a_tool_schema_lists_its_required_properties()
    {
        foreach (var tool in ResearchTools.Definitions)
        {
            foreach (var schema in Objects(tool.InputSchema))
            {
                Assert.True(
                    schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array,
                    $"{tool.Name} has an object schema without a required array.");
            }
        }
    }

    [Theory]
    [InlineData("Covey stable Douglass", "Covey OR stable OR Douglass")]
    [InlineData("Covey", null)]
    [InlineData("\"resolved to fight\" Covey", null)]
    [InlineData("Covey OR Covy", null)]
    [InlineData("Covey -Auld", null)]
    public void A_search_of_plain_words_that_finds_nothing_retries_with_any_of_them(string query, string? loose) =>
        Assert.Equal(loose, ResearchTools.AnyWord(query));

    private static IEnumerable<JsonElement> Objects(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (schema.TryGetProperty("type", out var type) && type.GetString() == "object")
        {
            yield return schema;
        }

        foreach (var child in schema.EnumerateObject().SelectMany(p => p.Value.ValueKind == JsonValueKind.Object
                     ? p.Value.EnumerateObject().Select(q => q.Value).Prepend(p.Value)
                     : []))
        {
            foreach (var nested in Objects(child))
            {
                yield return nested;
            }
        }
    }
}
