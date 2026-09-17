using System.Text.Json.Serialization;
using ProtoFast.Segmentation.Pipeline.Agents;

namespace ProtoFast.Segmentation.ContractTests;

/// <summary>
/// What comes back from a model is text, and the rules say it should be JSON. These cover the
/// ways it is not (plan §10.4) — each one either has to parse anyway or produce an error precise
/// enough to repair from.
/// </summary>
public class ModelJsonTests
{
    private sealed record Reply
    {
        [JsonPropertyName("window")]
        public int Window { get; init; }

        [JsonPropertyName("labels")]
        public IReadOnlyList<string> Labels { get; init; } = [];
    }

    [Fact]
    public void PlainJsonParses()
    {
        var result = ModelJson.Parse<Reply>("""{"window":3,"labels":["a","b"]}""");

        Assert.True(result.Success);
        Assert.Equal(3, result.Value!.Window);
    }

    [Fact]
    public void AFencedReplyParses()
    {
        var result = ModelJson.Parse<Reply>(
            """
            ```json
            {"window": 3, "labels": []}
            ```
            """);

        Assert.True(result.Success);
        Assert.Equal(3, result.Value!.Window);
    }

    [Fact]
    public void AReplyPaddedWithProseParses()
    {
        var result = ModelJson.Parse<Reply>(
            "Here is the labelling you asked for:\n\n{\"window\": 7, \"labels\": []}\n\nLet me know!");

        Assert.True(result.Success);
        Assert.Equal(7, result.Value!.Window);
    }

    [Fact]
    public void ABraceInsideAStringDoesNotEndTheScan()
    {
        var result = ModelJson.Parse<Reply>("""{"window": 1, "labels": ["a } b", "c \" d"]}""");

        Assert.True(result.Success);
        Assert.Equal(["a } b", "c \" d"], result.Value!.Labels);
    }

    [Fact]
    public void TrailingCommasAndCommentsAreTolerated()
    {
        // Not to be generous — to avoid spending a repair round on something with one right answer.
        var result = ModelJson.Parse<Reply>(
            """
            {
              // the window this covers
              "window": 2,
              "labels": ["a",],
            }
            """);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Window);
    }

    [Fact]
    public void AReplyWithNoJsonIsAnErrorTheRepairLoopCanUse()
    {
        var result = ModelJson.Parse<Reply>("I could not complete that request.");

        Assert.False(result.Success);
        Assert.Contains("no JSON object", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedJsonReportsWhereItStopped()
    {
        var result = ModelJson.Parse<Reply>("""{"window": 2, "labels": ["a", "b""");

        Assert.False(result.Success);
        Assert.Contains("did not parse", result.Error!, StringComparison.Ordinal);
    }
}
