using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>One model call per scene. A reply that is not the JSON asked for is asked for once more.</summary>
public sealed class SceneReader(ILanguageModelFactory models, IOptions<WorldModelBuildOptions> options, ILogger<SceneReader> logger)
{
    public async Task<SceneRead> ReadAsync(
        StorySnapshot story, SceneInfo scene, GraphSnapshot graph, FoldedGraph facts, IReadOnlySet<Guid> flashbacks, CancellationToken ct)
    {
        var prompt = WorldModelPrompt.Build(story, scene, graph, facts, flashbacks);
        var model = options.Value.Provider is { Length: > 0 } provider
            ? models.For(options.Value.ModelClass, provider)
            : models.For(options.Value.ModelClass);

        var started = Stopwatch.GetTimestamp();
        var reply = await model.CompleteAsync(WorldModelPrompt.System, prompt, ct);
        try
        {
            return new SceneRead(prompt, reply, Parse(reply.Text), Stopwatch.GetElapsedTime(started));
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Scene {SceneId} came back as something other than the JSON asked for; asking again", scene.Id);
        }

        var again = await model.CompleteAsync(WorldModelPrompt.System, prompt, ct);
        var combined = again with { Cost = reply.Cost + again.Cost, InputTokens = reply.InputTokens + again.InputTokens, OutputTokens = reply.OutputTokens + again.OutputTokens };
        return new SceneRead(prompt, combined, Parse(again.Text), Stopwatch.GetElapsedTime(started));
    }

    public static SceneReading Parse(string text) => StoryJson.Deserialize<SceneReading>(StoryJson.ExtractObject(text));
}
