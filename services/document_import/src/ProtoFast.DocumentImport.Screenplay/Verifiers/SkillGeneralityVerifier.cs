using System.Text;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <summary>A model's read of whether a skill fits every document of its family or only the one in hand.</summary>
public sealed class SkillGeneralityVerifier(
    ILanguageModelFactory models,
    SkillVerificationOptions options,
    LanguageModelOptions modelOptions,
    ILogger<SkillGeneralityVerifier> logger) : ISkillVerifier
{
    public const string VerifierId = "skill-generality";

    private const string System = """
        An import agent keeps what it learns about a document family as skills: instructions it reads
        and C# scripts it runs on the next document of the family. You review a skill before it is kept.
        Later documents of the family share its conventions but not its content: each has its own
        title, characters, places, wording, page furniture and quirks.

        A skill generalizes when its instructions and scripts describe the family's conventions and
        handle their variations by rule, and anything particular to a document is found in that document
        at run time - by pattern, or as labels a model passes to a script as args.

        A skill is overfitted when any part of it only works for one document: a script or section meant
        for one document ("only run it on ..."), a script named after one; the document's title,
        character or place names; its exact lines or passages; a table of fixes keyed by its lines; its
        page headers, counts or act and scene totals; facts like "this draft yields ...". An example
        that illustrates a general rule is fine when nothing depends on it.

        Reply with only a JSON object:

        { "verdict": "Pass|Degraded|Fail", "reason": "one sentence", "findings": [{ "path": "description|instructions|scripts/<name>", "message": "what is tied to one document, and how to generalize it" }] }

        Pass: nothing in it is tied to one document. Degraded: a few one-document examples, but no
        behaviour depends on them. Fail: instructions or code whose behaviour depends on one document,
        or a script or section meant for one document. Give one finding per problem; a pass has none.
        """;

    public string Id => VerifierId;

    public bool IsDeterministic => false;

    public async Task<VerifierResult> VerifyAsync(SkillReview review, CancellationToken ct)
    {
        var skill = review.Skill;
        var user = new StringBuilder()
            .AppendLine($"<family>{review.DocumentSignature.Family}</family>")
            .AppendLine("<document-in-hand>");
        foreach (var (facet, value) in review.DocumentSignature.Facets)
        {
            user.AppendLine($"{facet}: {value}");
        }

        user.AppendLine("</document-in-hand>").AppendLine()
            .AppendLine($"<skill name=\"{skill.Ref.Id}\">")
            .AppendLine($"<description>{skill.Description}</description>")
            .AppendLine("<instructions>").AppendLine(skill.Instructions).AppendLine("</instructions>");
        foreach (var script in skill.Scripts)
        {
            user.AppendLine($"<script name=\"{script.Name}\" description=\"{script.Description}\">")
                .AppendLine(review.Sources.GetValueOrDefault(script.Name))
                .AppendLine("</script>");
        }

        var prompt = user.AppendLine("</skill>").ToString();
        if (prompt.Length > modelOptions.MaxSourceChars)
        {
            prompt = prompt[..modelOptions.MaxSourceChars] + "\n[cut]";
        }

        var reply = await models.For(options.JudgeModelClass).CompleteAsync(System, prompt, ct);
        logger.LogInformation(
            "{Verifier} ran {Model} on {Skill} for {RunId}: {In} in, {Out} out",
            Id, reply.ModelId, skill.Ref.Id, review.RunId, reply.InputTokens, reply.OutputTokens);

        var judgement = StoryJson.Deserialize<RubricJudgement>(StoryJson.ExtractObject(reply.Text));
        if (!Enum.TryParse<Verdict>(judgement.Verdict, ignoreCase: true, out var verdict) || !Enum.IsDefined(verdict))
        {
            throw new InvalidOperationException($"The judge answered '{judgement.Verdict}', not a verdict.");
        }

        return new VerifierResult(Id, verdict, judgement.Reason ?? "", judgement.Findings ?? []);
    }
}
