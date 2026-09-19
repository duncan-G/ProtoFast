using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Items;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>One window of displayable paragraphs, with overlap context as phase 3 uses for lines.</summary>
public sealed record ItemWindow(
    int WindowIndex,
    IReadOnlyList<Paragraph> Paragraphs,
    int CommitStart,
    int CommitEndExclusive)
{
    /// <summary>
    /// The paragraphs this window's answer is kept for. <b>Items never cross a paragraph, and each
    /// paragraph is committed by exactly one window</b> — the invariant the planner guarantees,
    /// which is why phase 8 is a fan-out and not an orchestration (§8.2).
    /// </summary>
    public IReadOnlyList<Paragraph> Committed => [.. Paragraphs.Skip(CommitStart).Take(CommitEndExclusive - CommitStart)];
}

/// <summary>The typer's wire format: cuts and candidate tags, per paragraph. Never text.</summary>
public sealed record ItemTypingReply
{
    [JsonPropertyName("paragraphs")]
    public IReadOnlyList<ParagraphItems> Paragraphs { get; init; } = [];
}

public sealed record ParagraphItems
{
    [JsonPropertyName("paragraphId")]
    public string ParagraphId { get; init; } = string.Empty;

    [JsonPropertyName("cuts")]
    public IReadOnlyList<CutProposalWire> Cuts { get; init; } = [];

    [JsonPropertyName("tags")]
    public IReadOnlyList<TagProposalWire> Tags { get; init; } = [];
}

/// <summary>
/// A cut point, not a span. The model names where an item <em>starts</em> and what kind it is, and
/// code builds the spans between consecutive cuts — which is what makes <c>item-coverage</c> and
/// <c>display-integrity</c> impossible to fail rather than expensive to repair.
/// </summary>
public sealed record CutProposalWire
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "description";

    /// <summary>As written: "she", "Dr. Vance", "Q:". Required for a speech cut, empty otherwise.</summary>
    [JsonPropertyName("speaker")]
    public string Speaker { get; init; } = string.Empty;

    /// <summary><c>embodied</c> or <c>disembodied</c>.</summary>
    [JsonPropertyName("embodiment")]
    public string Embodiment { get; init; } = "embodied";

    /// <summary><c>in-scene</c>, <c>audience</c> or <c>self</c>.</summary>
    [JsonPropertyName("addressee")]
    public string Addressee { get; init; } = "in-scene";

    [JsonPropertyName("voiced")]
    public bool Voiced { get; init; } = true;

    /// <summary>False when the span cannot be staged on its own — the render-text selector (§3.6).</summary>
    [JsonPropertyName("standalone")]
    public bool Standalone { get; init; } = true;
}

/// <summary>A candidate tag: a surface form over a range, with no referent (§8.4 step 1).</summary>
public sealed record TagProposalWire
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "persona";

    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("end")]
    public int End { get; init; }

    [JsonPropertyName("surfaceForm")]
    public string SurfaceForm { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.5;
}

/// <summary>What one typing window produced.</summary>
public sealed record ItemWindowResult(
    int WindowIndex,
    IReadOnlyList<SceneItem> Items,
    string? ModelKey);

/// <summary>
/// Phase 8 (scene plan §8.6): partitions displayable paragraphs into typed items with candidate
/// tags.
///
/// <para>It <b>splits mixed-kind sentences</b> (§3.3) — <c>James shouted, "Bye" as he ran for the
/// door.</c> is one Speech item and one Action item — and it <b>never writes text</b>. Render text
/// belongs to the <c>render-text</c> augmentation, after the freeze (§3.6), and phase 8's only
/// contribution to it is the <c>IsStandalone</c> flag that selects which items the re-writer will
/// ever see.</para>
///
/// <para>Deterministic pre-segmentation runs first, so the model sees only genuinely ambiguous
/// prose (§4.5's second lever). Tagging costs output tokens rather than a second traversal, because
/// it runs inside the pass that already reads every displayable sentence — the third.</para>
/// </summary>
public sealed class ItemTyperAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<ItemTyperAgent> logger)
{
    private readonly RepairOptions _repair = options.Value.Repair;

    public async Task<ItemWindowResult> TypeAsync(
        ItemWindow window,
        SceneContext context,
        string compositionFamily,
        int itemCounter,
        int tagCounter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);

        var committed = window.Committed;
        var ambiguous = committed.Where(p => !ItemSegmenter.IsSettled(p)).ToList();

        var replies = new Dictionary<string, ParagraphItems>(StringComparer.Ordinal);
        string? modelKey = null;

        if (ambiguous.Count > 0)
        {
            var rendered = Render(window, ambiguous);

            var prompt = new PromptTemplate(runner.Assets.Template("item-typing.v1"))
                .Set("rules", runner.Assets.Rules)
                .Set("skill", runner.Assets.Skill("item-typing"))
                .Set("schema", runner.Assets.Schema("items"))
                .Set("familySkill", runner.Assets.FamilySkill(compositionFamily))
                .Set("compositionFamily", compositionFamily)
                .Set("instincts", context.RenderInstincts(InstinctScope.ItemType))
                .Set("window", rendered)
                .Render();

            var byId = ambiguous.ToDictionary(p => p.ParagraphId, StringComparer.Ordinal);

            var result = await runner.RunOrDegradeAsync<ItemTypingReply>(
                prompt,
                Routing(context, window, rendered.Length),
                reply => Validate(reply, byId),
                _repair.MaxRoundsPerArtifact,
                ct);

            if (result.Success)
            {
                foreach (var paragraph in result.Value!.Paragraphs)
                {
                    replies[paragraph.ParagraphId] = paragraph;
                }

                modelKey = result.Response?.Decision.Model.Key;
            }
            else
            {
                // K7 in the small: the deterministic pre-segmentation is a complete partition on its
                // own, so a window the model could not type still produces items. What is lost is
                // the sub-sentence splits and the candidate tags, not the coverage.
                logger.LogWarning(
                    "Run {RunId}: item window {Window} fell back to the deterministic partition ({Check}).",
                    context.RunId, window.WindowIndex, result.Validation.CheckId);
            }
        }

        var items = new List<SceneItem>();

        foreach (var paragraph in committed)
        {
            var reply = replies.GetValueOrDefault(paragraph.ParagraphId);

            var cuts = reply is { Cuts.Count: > 0 }
                ? [.. reply.Cuts.Select(ToCut)]
                : ItemSegmenter.PreSegment(paragraph);

            var tags = reply is null ? [] : ToTags(reply.Tags);

            var materialized = ItemMaterializer.Materialize(
                paragraph, cuts, tags, ref itemCounter, ref tagCounter);

            if (!materialized.Success)
            {
                // A plan that is not a set of cut points is discarded for that paragraph alone, and
                // the deterministic partition stands. A bad answer about one paragraph is not a
                // reason to fail a window.
                logger.LogWarning(
                    "Run {RunId}: {Paragraph} kept its deterministic items — {Errors}",
                    context.RunId, paragraph.ParagraphId, materialized.Validation.CheckId);

                materialized = ItemMaterializer.Materialize(
                    paragraph, ItemSegmenter.PreSegment(paragraph), [], ref itemCounter, ref tagCounter);
            }

            items.AddRange(materialized.Items);
        }

        return new ItemWindowResult(window.WindowIndex, items, modelKey);
    }

    private static ItemCut ToCut(CutProposalWire wire) =>
        new(
            wire.Start,
            Enum.TryParse<ItemKind>(wire.Kind, ignoreCase: true, out var kind) ? kind : ItemKind.Description,
            Enum.TryParse<ItemKind>(wire.Kind, ignoreCase: true, out var speechKind) && speechKind == ItemKind.Speech
                ? new SpeechAttributes(
                    wire.Speaker,
                    SpeakerPersonaId: null,
                    Enum.TryParse<Embodiment>(wire.Embodiment, ignoreCase: true, out var embodiment)
                        ? embodiment
                        : Embodiment.Embodied,
                    ParseAddressee(wire.Addressee),
                    wire.Voiced)
                : null,
            wire.Standalone);

    /// <summary>The wire spells it <c>in-scene</c>; the enum cannot carry a hyphen.</summary>
    private static Addressee ParseAddressee(string value) => value.ToLowerInvariant() switch
    {
        "in-scene" or "inscene" => Addressee.InScene,
        "self" => Addressee.Self,
        _ => Addressee.Audience,
    };

    private static IReadOnlyList<TagProposal> ToTags(IReadOnlyList<TagProposalWire> wire) =>
    [
        .. wire
            .Where(t => Enum.TryParse<TagKind>(Normalize(t.Kind), ignoreCase: true, out _))
            .Select(t => new TagProposal(
                Enum.Parse<TagKind>(Normalize(t.Kind), ignoreCase: true),
                t.Start,
                t.End,
                t.SurfaceForm,
                t.Confidence)),
    ];

    private static string Normalize(string kind) =>
        kind.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

    private static ValidationResult Validate(
        ItemTypingReply reply, IReadOnlyDictionary<string, Paragraph> committed)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var paragraph in reply.Paragraphs)
        {
            if (!committed.TryGetValue(paragraph.ParagraphId, out var source))
            {
                errors.Add(
                    $"'{paragraph.ParagraphId}' is not a paragraph this window commits — a window that "
                    + "typed a context paragraph has taken work belonging to another one");
                continue;
            }

            if (!seen.Add(paragraph.ParagraphId))
            {
                errors.Add($"'{paragraph.ParagraphId}' was typed twice");
            }

            foreach (var cut in paragraph.Cuts)
            {
                if (cut.Start < 0 || cut.Start >= source.Text.Length)
                {
                    errors.Add(
                        $"{paragraph.ParagraphId}: a cut at {cut.Start} is outside the paragraph "
                        + $"(0..{source.Text.Length - 1})");
                }

                if (!Enum.TryParse<ItemKind>(cut.Kind, ignoreCase: true, out _))
                {
                    errors.Add($"{paragraph.ParagraphId}: '{cut.Kind}' is not one of the five item kinds");
                }
            }

            foreach (var tag in paragraph.Tags)
            {
                if (tag.Start < 0 || tag.End > source.Text.Length || tag.End <= tag.Start)
                {
                    errors.Add(
                        $"{paragraph.ParagraphId}: tag '{tag.SurfaceForm}' spans {tag.Start}..{tag.End}, "
                        + $"outside the paragraph's 0..{source.Text.Length}");
                }
                else if (!string.Equals(
                    source.Text[tag.Start..tag.End].Trim(), tag.SurfaceForm.Trim(), StringComparison.Ordinal))
                {
                    // The surface form has to BE the text at those offsets. A tag whose form and
                    // offsets disagree is a tag pointing somewhere nobody intended, and it would
                    // bind a persona to the wrong words for the life of the document.
                    errors.Add(
                        $"{paragraph.ParagraphId}: tag says '{tag.SurfaceForm}' but characters "
                        + $"{tag.Start}..{tag.End} are '{source.Text[tag.Start..tag.End]}'");
                }
            }
        }

        return ValidationResult.Fail(Checks.Schema, errors);
    }

    /// <summary>
    /// The window as the model sees it: committed paragraphs with character offsets, and the
    /// surrounding paragraphs as read-only context so a pronoun opening a paragraph is not a mystery.
    /// </summary>
    private static string Render(ItemWindow window, IReadOnlyList<Paragraph> ambiguous)
    {
        var committed = ambiguous.Select(p => p.ParagraphId).ToHashSet(StringComparer.Ordinal);
        var builder = new StringBuilder();

        foreach (var paragraph in window.Paragraphs)
        {
            var isCommitted = committed.Contains(paragraph.ParagraphId);

            builder.Append("### ").Append(paragraph.ParagraphId)
                .AppendLine(isCommitted ? $"  ({paragraph.Text.Length} characters)" : "  (context only — do not type)");

            builder.AppendLine("```");
            builder.AppendLine(paragraph.Text);
            builder.AppendLine("```");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private RoutingContext Routing(SceneContext context, ItemWindow window, int renderedChars) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.TypeItems, AgentRole.ItemTyper,
            ModelTier.Mid, context.Sensitivity,
            EstimatedInputTokens: 2_000 + renderedChars / 4,
            // Items are sub-sentence and tags fire per referring expression, so the output scales
            // with characters rather than with paragraphs — which is the cost §4.5 is about.
            MaxOutputTokens: Math.Max(4_096, renderedChars / 8))
        {
            PinnedModelKey = context.PinnedKey(AgentRole.ItemTyper),
            PromptVersion = runner.Assets.VersionFor(AgentRole.ItemTyper),
            Unit = $"items:{window.WindowIndex}",
            OutputSchema = runner.Assets.WireSchemaElement("items"),
            OutputSchemaName = "items",
        };
}
