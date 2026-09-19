using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Scenes;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>The cutter's wire format: a verdict per proposed boundary, and the two coordinates.</summary>
public sealed record SceneCutReply
{
    [JsonPropertyName("boundaries")]
    public IReadOnlyList<BoundaryVerdict> Boundaries { get; init; } = [];

    [JsonPropertyName("scenes")]
    public IReadOnlyList<SceneCoordinates> Scenes { get; init; } = [];
}

public sealed record BoundaryVerdict
{
    /// <summary>The item the boundary falls before — an id code issued, never an index it chose.</summary>
    [JsonPropertyName("beforeItemId")]
    public string BeforeItemId { get; init; } = string.Empty;

    [JsonPropertyName("keep")]
    public bool Keep { get; init; } = true;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed record SceneCoordinates
{
    /// <summary>The scene's first item, which is how a scene is named before it has an id.</summary>
    [JsonPropertyName("firstItemId")]
    public string FirstItemId { get; init; } = string.Empty;

    /// <summary>A place id from the run's registry, or empty for Void. Never a place name (C11).</summary>
    [JsonPropertyName("placeId")]
    public string PlaceId { get; init; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; init; } = string.Empty;

    /// <summary>One of the six continuity relations [unit §3.2].</summary>
    [JsonPropertyName("timeRelation")]
    public string TimeRelation { get; init; } = "unanchored";

    [JsonPropertyName("timeAnchor")]
    public string TimeAnchor { get; init; } = string.Empty;

    /// <summary>Generated metadata, clearly marked and never re-entering the content stream (C9).</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = [];
}

/// <summary>What the agent settled for one leaf section.</summary>
public sealed record SceneCutDecision(
    IReadOnlyList<BoundaryProposal> Boundaries,
    IReadOnlyDictionary<int, SceneAssignment> Assignments,
    string? ModelKey);

/// <summary>
/// Step 2 of phase 10 (scene plan §8.8): assign Setting and Subject, confirm or reject the
/// candidate boundaries, and apply the treated-vs-listed granularity rule [unit §4.4].
///
/// <para>The model is left only the two coordinates that genuinely need judgement. Mode came from
/// the item mix and Cast from resolved Speech speakers, both in code — <b>cutting first would have
/// forced the model to guess mode from raw prose and then be stuck with that guess</b> (§8.3).
/// </para>
///
/// <para>Setting arrives as a place <b>id</b> from the registry the tag layer already built. A model
/// that could name a place could invent one, and C11 makes fabricating a coordinate a validation
/// failure rather than a fallback.</para>
/// </summary>
public sealed class SceneCutterAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<SceneCutterAgent> logger)
{
    private readonly RepairOptions _repair = options.Value.Repair;

    public async Task<SceneCutDecision> CutAsync(
        string sectionId,
        IReadOnlyList<SceneItem> items,
        IReadOnlyDictionary<string, string> paragraphText,
        CutProposal proposal,
        Registries registries,
        SceneContext context,
        string compositionFamily,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(paragraphText);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(registries);
        ArgumentNullException.ThrowIfNull(context);

        if (items.Count == 0)
        {
            return new SceneCutDecision([], new Dictionary<int, SceneAssignment>(), null);
        }

        var rendered = Render(sectionId, items, paragraphText, proposal, registries);

        var prompt = new PromptTemplate(runner.Assets.Template("scene-cut.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("scene-cutting"))
            .Set("schema", runner.Assets.Schema("scene-cut"))
            .Set("familySkill", runner.Assets.FamilySkill(compositionFamily))
            .Set("compositionFamily", compositionFamily)
            .Set("instincts", context.RenderInstincts(InstinctScope.SceneCut))
            .Set("section", rendered)
            .Render();

        var itemIds = items.Select(i => i.ItemId).ToHashSet(StringComparer.Ordinal);
        var placeIds = registries.Places.Select(p => p.PlaceId).ToHashSet(StringComparer.Ordinal);

        var result = await runner.RunOrDegradeAsync<SceneCutReply>(
            prompt,
            Routing(context, sectionId, items.Count, rendered.Length),
            reply => Validate(reply, itemIds, placeIds),
            _repair.MaxRoundsPerArtifact,
            ct);

        if (!result.Success)
        {
            // K7: the deterministic boundaries stand and every coordinate takes its nothing-value.
            // The scenes are poorer, not missing — C1 always holds, and the uncertainty surfaces as
            // flags rather than as absent data.
            logger.LogWarning(
                "Run {RunId}: section {Section} kept its deterministic cuts ({Check}).",
                context.RunId, sectionId, result.Validation.CheckId);

            return new SceneCutDecision(proposal.Boundaries, new Dictionary<int, SceneAssignment>(), null);
        }

        var reply = result.Value!;
        var rejected = reply.Boundaries
            .Where(b => !b.Keep)
            .Select(b => b.BeforeItemId)
            .ToHashSet(StringComparer.Ordinal);

        var indexOf = items
            .Select((item, index) => (item.ItemId, index))
            .ToDictionary(x => x.ItemId, x => x.index, StringComparer.Ordinal);

        var kept = proposal.Boundaries
            // A marked boundary is evidence, and the agent does not get to overrule evidence any
            // more than the floor does (§8.8 rule 2). A Transition item said the scene changed.
            .Where(b => b.IsMarked || !rejected.Contains(items[b.ItemIndex].ItemId))
            .ToList();

        var starts = new List<int> { 0 };
        starts.AddRange(kept.Select(b => b.ItemIndex).Where(i => i > 0 && i < items.Count).Distinct().Order());

        var assignments = new Dictionary<int, SceneAssignment>();

        foreach (var scene in reply.Scenes)
        {
            if (!indexOf.TryGetValue(scene.FirstItemId, out var itemIndex))
            {
                continue;
            }

            var sceneIndex = starts.IndexOf(itemIndex);
            if (sceneIndex < 0)
            {
                // The agent described a scene whose start it also rejected. Dropping the assignment
                // rather than the boundary keeps the two answers from contradicting each other.
                continue;
            }

            assignments[sceneIndex] = new SceneAssignment(
                sceneIndex,
                string.IsNullOrWhiteSpace(scene.PlaceId) ? null : scene.PlaceId,
                scene.Subject.Trim(),
                new TimeValue(
                    string.IsNullOrWhiteSpace(scene.TimeAnchor) ? null : scene.TimeAnchor.Trim(),
                    Enum.TryParse<TimeRelation>(scene.TimeRelation, ignoreCase: true, out var relation)
                        ? relation
                        : Core.Model.TimeRelation.Unanchored,
                    scene.Evidence.Count > 0 ? Provenance.Stated([.. scene.Evidence]) : null),
                string.IsNullOrWhiteSpace(scene.Title) ? null : scene.Title.Trim(),
                [.. scene.Evidence]);
        }

        return new SceneCutDecision(kept, assignments, result.Response?.Decision.Model.Key);
    }

    private static ValidationResult Validate(
        SceneCutReply reply, IReadOnlySet<string> itemIds, IReadOnlySet<string> placeIds)
    {
        var errors = new List<string>();

        foreach (var boundary in reply.Boundaries.Where(b => !itemIds.Contains(b.BeforeItemId)))
        {
            errors.Add(
                $"'{boundary.BeforeItemId}' is not an item of this section — boundaries are named by ids "
                + "the pipeline issued (C11)");
        }

        foreach (var scene in reply.Scenes)
        {
            if (!itemIds.Contains(scene.FirstItemId))
            {
                errors.Add($"'{scene.FirstItemId}' is not an item of this section");
            }

            if (!string.IsNullOrWhiteSpace(scene.PlaceId) && !placeIds.Contains(scene.PlaceId))
            {
                errors.Add(
                    $"'{scene.PlaceId}' is not a place in the run's registry. A scene is located by a "
                    + "place the text already named and the tag layer already resolved, or it is Void — "
                    + "inventing a place is a validation failure, not a fallback (C11).");
            }

            if (!string.IsNullOrWhiteSpace(scene.TimeRelation)
                && !Enum.TryParse<TimeRelation>(scene.TimeRelation, ignoreCase: true, out _))
            {
                errors.Add($"'{scene.TimeRelation}' is not one of the six continuity relations");
            }

            foreach (var evidence in scene.Evidence.Where(e => !itemIds.Contains(e) && !e.StartsWith('P')))
            {
                errors.Add(
                    $"'{evidence}' is cited as evidence but is not an item or paragraph id this run issued");
            }
        }

        return ValidationResult.Fail(Checks.Schema, errors);
    }

    /// <summary>
    /// The section as the cutter sees it: items with their derived mode and cast, the boundaries
    /// code already proposed with the reason for each, and the places available to assign. The
    /// derivations are shown rather than hidden so the model is confirming a reading rather than
    /// producing one.
    /// </summary>
    private static string Render(
        string sectionId,
        IReadOnlyList<SceneItem> items,
        IReadOnlyDictionary<string, string> paragraphText,
        CutProposal proposal,
        Registries registries)
    {
        var builder = new StringBuilder();
        var boundaries = proposal.Boundaries.ToDictionary(b => b.ItemIndex);
        var personas = registries.Personas.ToDictionary(p => p.PersonaId, StringComparer.Ordinal);

        builder.Append("## Section ").AppendLine(sectionId);

        if (registries.Places.Count > 0)
        {
            builder.AppendLine("### Places in this run's registry");
            foreach (var place in registries.Places)
            {
                builder.Append("- ").Append(place.PlaceId).Append("  \"").Append(place.CanonicalName)
                    .AppendLine("\"");
            }

            builder.AppendLine();
        }

        builder.AppendLine("### Items");

        for (var i = 0; i < items.Count; i++)
        {
            if (boundaries.TryGetValue(i, out var boundary))
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"--- proposed boundary before {items[i].ItemId} ({boundary.Reason}"
                    + $"{(boundary.IsMarked ? ", marked — cannot be rejected" : string.Empty)}) ---");
            }

            var item = items[i];
            var text = paragraphText.GetValueOrDefault(item.ParagraphId, string.Empty);

            builder.Append(item.ItemId).Append("  [").Append(item.Kind).Append(']')
                .Append("  mode=").Append(proposal.ModeByItem.Count > i ? proposal.ModeByItem[i] : SceneMode.Exhibited);

            if (item.Speech is { SpeakerPersonaId: { } speaker })
            {
                builder.Append("  speaker=").Append(personas.TryGetValue(speaker, out var persona)
                    ? $"{speaker} ({persona.CanonicalName})"
                    : speaker);
            }

            builder.AppendLine();
            builder.Append("    ").AppendLine(Excerpt(item.SpanOf(text)));
        }

        return builder.ToString();
    }

    private static string Excerpt(string span)
    {
        var flat = span.ReplaceLineEndings(" ").Trim();
        return flat.Length <= MaxExcerpt ? flat : flat[..MaxExcerpt] + " …";
    }

    private const int MaxExcerpt = 220;

    private RoutingContext Routing(SceneContext context, string sectionId, int itemCount, int renderedChars) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.CutScenes, AgentRole.SceneCutter,
            ModelTier.Mid, context.Sensitivity,
            EstimatedInputTokens: 2_000 + renderedChars / 4,
            MaxOutputTokens: Math.Max(4_096, itemCount * 48))
        {
            PinnedModelKey = context.PinnedKey(AgentRole.SceneCutter),
            PromptVersion = runner.Assets.VersionFor(AgentRole.SceneCutter),
            Unit = $"scene-cut:{sectionId}",
            OutputSchema = runner.Assets.WireSchemaElement("scene-cut"),
            OutputSchemaName = "scene-cut",
        };
}
