using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline.Augmentation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>One paragraph's augmentation, ready to be written as an artifact and a result row.</summary>
public sealed record AugmentationOutput(
    string ParagraphId,
    string Type,
    string Json,
    string ReviewVerdict,
    string? ModelKey);

/// <summary>
/// Phases 10 and 11 (plan §12).
///
/// <para>Everything here reads from the frozen artifact, never from the working paragraphs. That
/// is the point of the freeze: augmentation is expensive and re-runnable, and it must be
/// impossible for a second augmentation pass to be describing a slightly different document than
/// the first one did.</para>
/// </summary>
public sealed class AugmenterAgent(AgentRunner runner, ILogger<AugmenterAgent> logger)
{
    public async Task<AugmentationOutput?> AugmentAsync(
        IAugmentationType type,
        AugmentationContext context,
        Paragraph paragraph,
        StructureContext structure,
        CancellationToken ct = default)
    {
        var skill = runner.Assets.Read(type.SkillPath);

        var prompt = new PromptTemplate(runner.Assets.Template("augmenter.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", skill)
            .Set("documentTitle", context.DocumentTitle)
            .Set("sectionPath", context.SectionPath.Count == 0 ? "(none)" : string.Join(" › ", context.SectionPath))
            .Set("previousParagraph", context.PreviousParagraphText ?? "(none)")
            .Set("nextParagraph", context.NextParagraphText ?? "(none)")
            .Set("paragraphId", context.ParagraphId)
            .Set("paragraphText", context.ParagraphText)
            .Set("schemaName", type.SchemaName + ".schema.json")
            .Render();

        var result = await runner.RunAsync<JsonElement>(
            prompt,
            new RoutingContext(
                structure.RunId, structure.DocumentId, PipelinePhase.Augment, AgentRole.Augmenter,
                type.Tier, structure.Sensitivity,
                EstimatedInputTokens: 800 + prompt.Length / 4,
                MaxOutputTokens: 1_024)
            {
                PromptVersion = runner.Assets.VersionFor(AgentRole.Augmenter),
                Unit = paragraph.ParagraphId,
            },
            output => Combine(type.Validate(paragraph, output)),
            maxRounds: 1, buildRepairPrompt: null, ct);

        if (!result.Success)
        {
            // A paragraph that will not augment is flagged in ThePlot rather than failing the run:
            // the structure — which is the product — is already frozen and correct.
            logger.LogWarning(
                "Augmentation '{Type}' failed for {ParagraphId} in run {RunId}: {Report}",
                type.Name, paragraph.ParagraphId, structure.RunId, result.Validation.ErrorReport);
            return null;
        }

        return new AugmentationOutput(
            paragraph.ParagraphId,
            type.Name,
            result.Value.GetRawText(),
            ReviewVerdict: "unreviewed",
            result.Response?.Decision.Model.Key);
    }

    /// <summary>
    /// The reviewer pass of plan §12.3, run on a sample. A failed review regenerates once with the
    /// reviewer's own notes as feedback, then gives up and flags — a second regeneration of
    /// something two models disagree about is spend, not signal.
    /// </summary>
    public async Task<AugmentationOutput> ReviewAsync(
        IAugmentationType type,
        AugmentationOutput output,
        Paragraph paragraph,
        AugmentationContext context,
        StructureContext structure,
        CancellationToken ct = default)
    {
        var prompt = new PromptTemplate(runner.Assets.Template("augment-reviewer.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("paragraphId", paragraph.ParagraphId)
            .Set("paragraphText", paragraph.Text)
            .Set("augmentation", output.Json)
            .Render();

        var result = await runner.RunAsync<ReviewReply>(
            prompt,
            new RoutingContext(
                structure.RunId, structure.DocumentId, PipelinePhase.ReviewAugmentation,
                AgentRole.AugmentReviewer, ModelTier.Mid, structure.Sensitivity,
                EstimatedInputTokens: 600 + prompt.Length / 4,
                MaxOutputTokens: 1_024)
            {
                // The producer's provider is avoided so the review is a second opinion rather than
                // the same model agreeing with itself.
                AvoidProvider = ProviderOf(output.ModelKey),
                PromptVersion = runner.Assets.VersionFor(AgentRole.AugmentReviewer),
                Unit = paragraph.ParagraphId,
            },
            _ => ValidationResult.Pass(Checks.Schema),
            maxRounds: 1, buildRepairPrompt: null, ct);

        if (!result.Success || result.Value is null)
        {
            return output with { ReviewVerdict = "review-unavailable" };
        }

        var verdict = result.Value.Verdict;
        if (verdict != "fail")
        {
            return output with { ReviewVerdict = verdict };
        }

        var notes = string.Join("; ", result.Value.Findings.Select(f => f.Message));
        logger.LogInformation(
            "Regenerating augmentation for {ParagraphId} after review: {Notes}", paragraph.ParagraphId, notes);

        var regenerated = await AugmentAsync(type, context with
        {
            ParagraphText = context.ParagraphText,
        }, paragraph, structure, ct);

        return regenerated is null
            ? output with { ReviewVerdict = "failed-review" }
            : regenerated with { ReviewVerdict = "regenerated" };
    }

    private static string? ProviderOf(string? modelKey) =>
        modelKey?.Split('/', 2) is [var provider, _] ? provider : null;

    private static ValidationResult Combine(IEnumerable<ValidationResult> results)
    {
        var failures = results.Where(r => !r.Passed).ToList();
        return failures.Count == 0
            ? ValidationResult.Pass(Checks.AugGrounding)
            : ValidationResult.Fail(failures[0].CheckId, failures.SelectMany(f => f.Errors));
    }
}
