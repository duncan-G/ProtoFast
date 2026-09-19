using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Classification;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>The classifier's wire format: one verdict per candidate, with the ids that justify it.</summary>
public sealed record PresentationReply
{
    [JsonPropertyName("verdicts")]
    public IReadOnlyList<PresentationVerdict> Verdicts { get; init; } = [];
}

public sealed record PresentationVerdict
{
    [JsonPropertyName("paragraphId")]
    public string ParagraphId { get; init; } = string.Empty;

    /// <summary><c>displayable</c> or one of the five metadata classes.</summary>
    [JsonPropertyName("class")]
    public string Class { get; init; } = "displayable";

    [JsonPropertyName("evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.5;
}

/// <summary>
/// Phase 5's agent (scene plan §8.5 step 3): a pass <b>over candidates only</b>, never the whole
/// document.
///
/// <para>It answers one question — is this paragraph part of the work? — and the asymmetry in §5.2
/// decides every close call: <b>under-removal is recoverable in review; over-removal silently loses
/// the work.</b> So the prompt asks for metadata to be named rather than for displayable text to be
/// defended, and anything uncertain comes back displayable and flagged.</para>
///
/// <para>Both families' <c>Metadata</c> instincts are injected, because the boundary is genuinely
/// family-dependent on both axes: "pages 1–4 are front matter in this publisher's template" is a
/// production fact, and "in this collection, epigraphs are displayable" is a composition one
/// (§7.2).</para>
/// </summary>
public sealed class PresentationClassifierAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options)
{
    private readonly RepairOptions _repair = options.Value.Repair;
    private readonly PresentationOptions _presentation = options.Value.Presentation;

    public async Task<(IReadOnlyDictionary<string, ParagraphPresentation> Answers, string? ModelKey)> ClassifyAsync(
        IReadOnlyList<PresentationCandidate> candidates,
        IReadOnlyDictionary<string, Paragraph> paragraphs,
        SceneContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(context);

        if (candidates.Count == 0)
        {
            // A document whose edges are plainly body text costs nothing here. The same shape as the
            // labelling short-circuit: the cheap path is the absence of a call, not a cheaper call.
            return (new Dictionary<string, ParagraphPresentation>(StringComparer.Ordinal), null);
        }

        var rendered = Render(candidates, paragraphs);

        var prompt = new PromptTemplate(runner.Assets.Template("presentation.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("presentation-classification"))
            .Set("schema", runner.Assets.Schema("presentation"))
            .Set("familySkill", runner.Assets.FamilySkill(context.ProductionFamily))
            .Set("compositionFamily", context.CompositionFamily)
            .Set("instincts", context.RenderInstincts(InstinctScope.Metadata))
            .Set("candidates", rendered)
            .Render();

        var expected = candidates.Select(c => c.ParagraphId).ToHashSet(StringComparer.Ordinal);

        var result = await runner.RunOrDegradeAsync<PresentationReply>(
            prompt,
            Routing(context, candidates.Count, rendered.Length),
            reply => Validate(reply, expected),
            _repair.MaxRoundsPerArtifact,
            ct);

        if (!result.Success)
        {
            // Not a failed run. Every candidate falls back to what code already believed, and code's
            // default is displayable — so a classifier that could not answer costs recall, never
            // the work itself (§5.2).
            return (new Dictionary<string, ParagraphPresentation>(StringComparer.Ordinal), null);
        }

        var answers = new Dictionary<string, ParagraphPresentation>(StringComparer.Ordinal);

        foreach (var verdict in result.Value!.Verdicts)
        {
            if (!expected.Contains(verdict.ParagraphId))
            {
                continue;
            }

            answers[verdict.ParagraphId] = Parse(verdict) is { } @class
                ? ParagraphPresentation.Metadata(
                    verdict.ParagraphId,
                    @class,
                    [.. verdict.Evidence.Where(e => e.Length > 0)],
                    verdict.Confidence)
                : ParagraphPresentation.Displayable(verdict.ParagraphId, verdict.Confidence);
        }

        return (answers, result.Response?.Decision.Model.Key);
    }

    /// <summary>
    /// A verdict below the confidence floor is read as displayable regardless of what it said. That
    /// is §8.5 step 4 in one line: default to displayable on uncertainty, and flag it.
    /// </summary>
    private MetadataClass? Parse(PresentationVerdict verdict) =>
        verdict.Confidence >= _presentation.UncertainConfidence
        && Enum.TryParse<MetadataClass>(verdict.Class, ignoreCase: true, out var parsed)
            ? parsed
            : null;

    private static ValidationResult Validate(PresentationReply reply, IReadOnlySet<string> expected)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var verdict in reply.Verdicts)
        {
            if (!expected.Contains(verdict.ParagraphId))
            {
                errors.Add(
                    $"'{verdict.ParagraphId}' was not one of the candidates — a verdict may only be given "
                    + "about a paragraph that was offered");
            }
            else if (!seen.Add(verdict.ParagraphId))
            {
                errors.Add($"'{verdict.ParagraphId}' was classified twice");
            }

            if (!string.Equals(verdict.Class, "displayable", StringComparison.OrdinalIgnoreCase)
                && !Enum.TryParse<MetadataClass>(verdict.Class, ignoreCase: true, out _))
            {
                errors.Add($"'{verdict.Class}' is not 'displayable' or one of the five metadata classes");
            }

            if (verdict.Confidence is < 0 or > 1)
            {
                errors.Add($"{verdict.ParagraphId}: confidence {verdict.Confidence} is outside 0..1");
            }
        }

        return ValidationResult.Fail(Checks.Schema, errors);
    }

    /// <summary>
    /// Each candidate with the reason code offered it, so the model is answering about something it
    /// can see rather than being asked to find the front matter itself.
    /// </summary>
    private static string Render(
        IReadOnlyList<PresentationCandidate> candidates, IReadOnlyDictionary<string, Paragraph> paragraphs)
    {
        var builder = new StringBuilder();

        foreach (var candidate in candidates)
        {
            builder.Append("### ").AppendLine(candidate.ParagraphId);
            builder.Append("why offered: ").AppendLine(string.Join("; ", candidate.Reasons));

            if (candidate.Suggested is { } suggested)
            {
                builder.Append("code's guess: ").AppendLine(suggested.ToString());
            }

            if (paragraphs.TryGetValue(candidate.ParagraphId, out var paragraph))
            {
                builder.AppendLine("```");
                builder.AppendLine(Excerpt(paragraph.Text));
                builder.AppendLine("```");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Enough to recognise a copyright page or a table of contents. A front-matter decision never
    /// needs the paragraph's whole text, and sending it would make the phase scale with the
    /// document rather than with its edges.
    /// </summary>
    private static string Excerpt(string text) =>
        text.Length <= MaxExcerpt ? text : text[..MaxExcerpt] + " …";

    private const int MaxExcerpt = 400;

    private RoutingContext Routing(SceneContext context, int candidateCount, int renderedChars) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.ClassifyPresentation,
            AgentRole.PresentationClassifier,
            // Mid: the question is "is this apparatus or is it the work", asked about a short
            // excerpt with the reason already supplied. It is a smaller job than structuring a
            // window, and a Large model here would be spend with no upside.
            ModelTier.Mid, context.Sensitivity,
            EstimatedInputTokens: 1_500 + renderedChars / 4,
            MaxOutputTokens: Math.Max(2_048, candidateCount * 64))
        {
            PinnedModelKey = context.PinnedKey(AgentRole.PresentationClassifier),
            PromptVersion = runner.Assets.VersionFor(AgentRole.PresentationClassifier),
            Unit = $"presentation:{candidateCount}",
            OutputSchema = runner.Assets.WireSchemaElement("presentation"),
            OutputSchemaName = "presentation",
        };
}
