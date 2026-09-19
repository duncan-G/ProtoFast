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
            .Set("schema", runner.Assets.Schema(type.SchemaName))
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
                OutputSchema = runner.Assets.WireSchemaElement(type.SchemaName),
                OutputSchemaName = type.SchemaName,
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
    /// One <b>item-scoped</b> augmentation call (scene plan §3.6, §10).
    ///
    /// <para>The same loop as the paragraph form, with the type's own <c>Validate</c> in the same
    /// place — which is the point: the re-writer's grounding gate sits exactly where
    /// <c>aug-grounding</c> sits, reading "its own target id" where that read "its own paragraph
    /// id". Nothing about fan-out, batching, idempotency or review sampling is different for an
    /// item.</para>
    ///
    /// <para>A rejected output returns null and the item keeps <c>RenderText = null</c>, so the
    /// renderer falls back to the span. That is K7, not a blocked run — which is what lets the
    /// grounding gate be hard without ever being able to fail a document.</para>
    /// </summary>
    public async Task<string?> AugmentItemAsync(
        IItemAugmentationType type,
        ItemAugmentationContext context,
        SceneContext scene,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scene);

        var span = context.Item.SpanOf(context.ParagraphText);

        var prompt = new PromptTemplate(runner.Assets.Template("augment-item.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Read(type.SkillPath))
            .Set("itemId", context.Item.ItemId)
            .Set("itemKind", context.Item.Kind.ToString())
            .Set("span", span)
            // Exactly the personas the item's OWN tags bind — which is what row two of the
            // grounding rule admits, so the prompt shows the model precisely what it may use.
            .Set("personas", context.ResolvedPersonas.Count == 0
                ? "(none — the span refers to nobody the tag layer resolved)"
                : string.Join(", ", context.ResolvedPersonas.Select(p => p.CanonicalName)))
            .Set("schemaName", type.SchemaName + ".schema.json")
            .Set("schema", runner.Assets.Schema(type.SchemaName))
            .Render();

        var result = await runner.RunOrDegradeAsync<JsonElement>(
            prompt,
            new RoutingContext(
                scene.RunId, scene.DocumentId, PipelinePhase.Augment, AgentRole.Augmenter,
                type.Tier, scene.Sensitivity,
                EstimatedInputTokens: 600 + prompt.Length / 4,
                MaxOutputTokens: 512)
            {
                PromptVersion = runner.Assets.VersionFor(AgentRole.Augmenter),
                Unit = context.Item.ItemId,
                OutputSchema = runner.Assets.WireSchemaElement(type.SchemaName),
                OutputSchemaName = type.SchemaName,
            },
            output => Combine(type.Validate(context, output)),
            // One regeneration, then the item keeps its span: "rejected, regenerated once, then
            // flagged" is the path the plan specifies behind the grounding gate (§3.7).
            maxRounds: 1,
            ct);

        if (!result.Success)
        {
            logger.LogDebug(
                "Augmentation '{Type}' was rejected for {ItemId} in run {RunId}: {Check}",
                type.Name, context.Item.ItemId, scene.RunId, result.Validation.CheckId);

            return null;
        }

        return result.Value.TryGetProperty("renderText", out var property) ? property.GetString() : null;
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
            .Set("schema", runner.Assets.Schema("review"))
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
