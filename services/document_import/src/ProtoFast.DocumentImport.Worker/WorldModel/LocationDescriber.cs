using System.Text.Json;
using Microsoft.Extensions.Options;
using ProtoFast.Data.ThePlot.WorldModel;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>One model call per location. A reply that is not the JSON asked for is asked for once more.</summary>
public sealed class LocationDescriber(ILanguageModelFactory models, IOptions<WorldModelBuildOptions> options, ILogger<LocationDescriber> logger)
{
    public async Task<LocationRead> DescribeAsync(
        StorySnapshot story, GraphSnapshot graph, FoldedGraph facts, LocationInfo location, (string Name, string Description)? outer,
        CancellationToken ct)
    {
        var prompt = LocationDescriptionPrompt.Build(story, graph, facts, location, outer);
        var model = options.Value.Provider is { Length: > 0 } provider
            ? models.For(options.Value.ModelClass, provider)
            : models.For(options.Value.ModelClass);

        var reply = await model.CompleteAsync(LocationDescriptionPrompt.System, prompt, ct);
        try
        {
            return new LocationRead(prompt, reply, Parse(reply.Text));
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Location {LocationId} came back as something other than the JSON asked for; asking again", location.Id);
        }

        var again = await model.CompleteAsync(LocationDescriptionPrompt.System, prompt, ct);
        var combined = again with { Cost = reply.Cost + again.Cost, InputTokens = reply.InputTokens + again.InputTokens, OutputTokens = reply.OutputTokens + again.OutputTokens };
        return new LocationRead(prompt, combined, Parse(again.Text));
    }

    public static LocationDescription Parse(string text) => StoryJson.Deserialize<LocationDescription>(StoryJson.ExtractObject(text));
}
