using System.Text;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Executors;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <summary>A document family's rubric, judged by a model reading the stage's inputs and output.</summary>
public sealed class RubricVerifier(
    VerifierSpec spec,
    ILanguageModel model,
    IArtifactStore artifacts,
    LanguageModelOptions options,
    ILogger logger) : IVerifier
{
    private const string System = """
        You judge one stage of a document import against a rubric. Read the rubric, the stage's inputs
        and its output, then reply with only a JSON object:

        { "verdict": "Pass|Degraded|Fail", "reason": "one sentence", "findings": [{ "path": "where in the output", "message": "what is wrong" }] }

        Pass: the output meets the rubric. Degraded: it meets the rubric with minor gaps. Fail: it does
        not, or what you were given is not enough to tell. Give one finding per problem; a pass has none.
        """;

    public string Id => spec.Id;

    public bool IsDeterministic => false;

    public async Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct)
    {
        var user = new StringBuilder()
            .AppendLine("<rubric>").AppendLine(spec.Rubric).AppendLine("</rubric>").AppendLine()
            .Append(await StagePrompt.InputsAsync(artifacts, request.Inputs, options.MaxSourceChars, ct))
            .AppendLine("<output>").AppendLine(await ArtifactText.ReadAsync(artifacts, result.Output, ct)).AppendLine("</output>")
            .ToString();

        var reply = await model.CompleteAsync(System, user, ct);
        logger.LogInformation(
            "{Verifier} ran {Model} for {RunId}/{StageId}: {In} in, {Out} out",
            Id, reply.ModelId, request.RunId, request.Stage.Id, reply.InputTokens, reply.OutputTokens);

        var judgement = StoryJson.Deserialize<RubricJudgement>(StoryJson.ExtractObject(reply.Text));
        if (!Enum.TryParse<Verdict>(judgement.Verdict, ignoreCase: true, out var verdict) || !Enum.IsDefined(verdict))
        {
            throw new InvalidOperationException($"The judge answered '{judgement.Verdict}', not a verdict.");
        }

        return new VerifierResult(Id, verdict, judgement.Reason ?? "", judgement.Findings ?? []);
    }
}
