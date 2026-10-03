using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Executors;

/// <summary>One model call per stage: the playbook is the system prompt, the inputs are the user turn.</summary>
public sealed class LanguageModelExecutor(
    ExecutorSpec spec,
    Playbook playbook,
    ILanguageModel model,
    IArtifactStore artifacts,
    LanguageModelOptions options,
    TimeProvider time,
    ILogger logger) : IExecutor
{
    public Tier Tier => spec.Tier;

    public async Task<StageResult> ExecuteAsync(StageRequest request, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var user = await StagePrompt.BuildAsync(artifacts, request, options.MaxSourceChars, corrections: null, ct);
        var reply = await model.CompleteAsync(SystemPrompt(), user, ct);
        logger.LogInformation(
            "{Executor} ran {Model} for {RunId}/{StageId}: {In} in, {Out} out",
            spec.Ref, reply.ModelId, request.RunId, request.Stage.Id, reply.InputTokens, reply.OutputTokens);

        var output = await artifacts.PutAsync(
            request.RunId, request.Stage.Id, StoryJson.ToStream(StoryJson.ExtractObject(reply.Text)), request.Stage.Output, ct);

        return new StageResult(
            output,
            Trace: null,
            new Cost(reply.Cost, time.GetElapsedTime(started)),
            [new Decision("model", reply.ModelId, $"{spec.ModelClass} class on the configured provider.", 1)]);
    }

    private string SystemPrompt()
    {
        if (playbook.Rules.Count == 0)
        {
            return playbook.Instructions;
        }

        return playbook.Instructions + "\n\nRules learned from earlier runs:\n" +
               string.Join('\n', playbook.Rules.Select(r => $"- {r.Key}: {r.Value}"));
    }
}
