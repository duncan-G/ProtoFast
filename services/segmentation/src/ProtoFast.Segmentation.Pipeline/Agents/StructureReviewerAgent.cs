using System.Text;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// Phase 7 (plan §9.9, §10.3): a second opinion on the tree, from fresh context and — where the
/// registry allows it — a different provider than the one that built it.
///
/// <para>The evidence it is given is deliberately narrow: for each boundary, the last sentence
/// before and the first sentence after. That pair is what actually decides whether a boundary is
/// wrong, and sending the whole document instead would cost a large-model pass over the corpus to
/// answer a question that two sentences answer.</para>
/// </summary>
public sealed class StructureReviewerAgent(AgentRunner runner, ILogger<StructureReviewerAgent> logger)
{
    public async Task<(IReadOnlyList<Finding> Findings, string Verdict)> ReviewAsync(
        SectionNode root,
        IReadOnlyList<Paragraph> paragraphs,
        StructureContext context,
        string? producerProvider,
        CancellationToken ct = default)
    {
        var byId = paragraphs.ToDictionary(p => p.ParagraphId, StringComparer.Ordinal);

        var prompt = new PromptTemplate(runner.Assets.Template("structure-reviewer.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("structure-review"))
            .Set("schema", runner.Assets.Schema("review"))
            .Set("outline", RenderOutline(root))
            .Set("boundaries", RenderBoundaries(root, byId))
            .Set("inferredTitles", RenderInferredTitles(root, byId))
            .Render();

        var result = await runner.RunAsync<ReviewReply>(
            prompt,
            new RoutingContext(
                context.RunId, context.DocumentId, PipelinePhase.ReviewStructure, AgentRole.StructureReviewer,
                // Restricted documents get the large tier: they are the ones whose review has to
                // stand on its own, because a human gate is mandatory for them anyway and a weak
                // review wastes that person's time (plan §10.3).
                context.Sensitivity == Sensitivity.Restricted ? ModelTier.Large : ModelTier.Mid,
                context.Sensitivity,
                EstimatedInputTokens: 1_500 + prompt.Length / 4,
                MaxOutputTokens: 2_048)
            {
                AvoidProvider = producerProvider,
                PromptVersion = runner.Assets.VersionFor(AgentRole.StructureReviewer),
                Unit = "tree",
                OutputSchema = runner.Assets.WireSchemaElement("review"),
                OutputSchemaName = "review",
            },
            reply => Validate(reply, root, byId),
            maxRounds: 1, buildRepairPrompt: null, ct);

        if (!result.Success)
        {
            // A review that will not parse is not a reason to block a tree that already passed
            // every deterministic check. It is recorded as a finding of its own so the gap is
            // visible rather than silent.
            logger.LogWarning(
                "Structure review for run {RunId} did not validate: {Report}",
                context.RunId, result.Validation.ErrorReport);

            return (
                [new Finding(FindingSeverity.Low, "other", ["review"], "The structure reviewer did not return a usable review.")],
                "pass_with_findings");
        }

        return (result.Value!.ToFindings(), result.Value.Verdict);
    }

    /// <summary>
    /// The only thing worth validating here is that the reviewer is talking about this document.
    /// A finding naming an id that does not exist is not a finding, and acting on it would move a
    /// boundary at random.
    /// </summary>
    private static ValidationResult Validate(
        ReviewReply reply, SectionNode root, IReadOnlyDictionary<string, Paragraph> paragraphs)
    {
        var known = root.Descend().Select(n => n.SectionId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in paragraphs.Keys)
        {
            known.Add(id);
        }

        var errors = new List<string>();
        foreach (var finding in reply.Findings)
        {
            foreach (var id in finding.Ids.Where(id => !known.Contains(id)))
            {
                errors.Add($"finding references unknown id '{id}'");
            }
        }

        if (reply.Verdict is not ("pass" or "pass_with_findings" or "fail"))
        {
            errors.Add($"verdict '{reply.Verdict}' is not one of pass | pass_with_findings | fail");
        }

        return ValidationResult.Fail(Checks.Schema, errors);
    }

    private static string RenderOutline(SectionNode root)
    {
        var builder = new StringBuilder();
        Walk(root, 0, builder);
        return builder.ToString();

        static void Walk(SectionNode node, int depth, StringBuilder builder)
        {
            builder
                .Append(new string(' ', depth * 2))
                .Append(node.SectionId).Append(' ')
                .Append('"').Append(node.Title).Append('"');

            if (node.TitleInferred)
            {
                builder.Append(" [inferred]");
            }

            if (node.ParagraphIds.Count > 0)
            {
                builder.Append(" — ").Append(node.ParagraphIds.Count).Append(" paragraphs");
            }

            builder.AppendLine();

            foreach (var child in node.Children)
            {
                Walk(child, depth + 1, builder);
            }
        }
    }

    /// <summary>For each section start: the sentence before it, and the sentence after it.</summary>
    private static string RenderBoundaries(SectionNode root, IReadOnlyDictionary<string, Paragraph> paragraphs)
    {
        var ordered = root.Descend().Where(n => n.ParagraphIds.Count > 0).ToList();
        var builder = new StringBuilder();

        for (var i = 1; i < ordered.Count; i++)
        {
            var before = ordered[i - 1].ParagraphIds[^1];
            var after = ordered[i].ParagraphIds[0];

            builder
                .Append(ordered[i - 1].SectionId).Append(" -> ").Append(ordered[i].SectionId).AppendLine(":")
                .Append("  last before  (").Append(before).Append("): ").AppendLine(LastSentence(paragraphs, before))
                .Append("  first after  (").Append(after).Append("): ").AppendLine(FirstSentence(paragraphs, after));
        }

        return builder.ToString();
    }

    private static string RenderInferredTitles(SectionNode root, IReadOnlyDictionary<string, Paragraph> paragraphs)
    {
        var builder = new StringBuilder();

        foreach (var node in root.Descend().Where(n => n is { TitleInferred: true, ParagraphIds.Count: > 0 }))
        {
            builder
                .Append(node.SectionId).Append(" \"").Append(node.Title).AppendLine("\":")
                .Append("  first: ").AppendLine(FirstSentence(paragraphs, node.ParagraphIds[0]))
                .Append("  last:  ").AppendLine(LastSentence(paragraphs, node.ParagraphIds[^1]));
        }

        return builder.ToString();
    }

    private static string FirstSentence(IReadOnlyDictionary<string, Paragraph> paragraphs, string id) =>
        paragraphs.TryGetValue(id, out var paragraph)
            ? SentenceSplitter.Split(paragraph.Text).FirstOrDefault() ?? paragraph.Text
            : "(missing)";

    private static string LastSentence(IReadOnlyDictionary<string, Paragraph> paragraphs, string id) =>
        paragraphs.TryGetValue(id, out var paragraph)
            ? SentenceSplitter.Split(paragraph.Text).LastOrDefault() ?? paragraph.Text
            : "(missing)";
}
