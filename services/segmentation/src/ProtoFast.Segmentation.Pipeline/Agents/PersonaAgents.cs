using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Personas;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>One window of displayable paragraphs with their typed items, as phase 9 reads them.</summary>
public sealed record PersonaWindow(
    int WindowIndex,
    IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<SceneItem> Items,
    string? SectionId);

/// <summary>The windower's wire format: local clusters, and what it cannot settle alone.</summary>
public sealed record PersonaWindowReply
{
    [JsonPropertyName("candidates")]
    public IReadOnlyList<CandidateWire> Candidates { get; init; } = [];

    [JsonPropertyName("openQuestions")]
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
}

public sealed record CandidateWire
{
    /// <summary><c>persona</c>, <c>group</c>, <c>place</c> or <c>exhibit-ref</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "persona";

    [JsonPropertyName("surfaceForm")]
    public string SurfaceForm { get; init; } = string.Empty;

    [JsonPropertyName("surfaceForms")]
    public IReadOnlyList<string> SurfaceForms { get; init; } = [];

    /// <summary>The tag ids this cluster covers. Issued by code — a tag id the reply invented is rejected.</summary>
    [JsonPropertyName("tagIds")]
    public IReadOnlyList<string> TagIds { get; init; } = [];

    /// <summary><c>enumerated</c> or <c>named</c>, for a group (§4.3).</summary>
    [JsonPropertyName("formation")]
    public string Formation { get; init; } = string.Empty;

    /// <summary>Surface forms of the members this reference resolved, for an enumerated group.</summary>
    [JsonPropertyName("members")]
    public IReadOnlyList<string> Members { get; init; } = [];

    [JsonPropertyName("membershipComplete")]
    public bool MembershipComplete { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.5;
}

/// <summary>
/// Round 0 of phase 9 (scene plan §8.7): one windower sees a run of displayable paragraphs with
/// their typed items and candidate tags, and returns <em>local</em> referents — clusters of surface
/// forms it is confident co-refer inside its own window.
///
/// <para>It never resolves across its own edge. What it cannot settle alone becomes an
/// <c>openQuestion</c> — "this candidate is introduced by a pronoun before my first paragraph" —
/// which is precisely the case the orchestrator exists to settle and which no windowing removes.
/// </para>
/// </summary>
public sealed class PersonaWindowerAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<PersonaWindowerAgent> logger)
{
    private readonly RepairOptions _repair = options.Value.Repair;

    /// <summary>Enough for the orchestrator to see what a window is unsure about; few enough to read.</summary>
    public const int MaxOpenQuestions = 5;

    public async Task<(CandidateWindow Window, string? ModelKey)> ClusterAsync(
        PersonaWindow window,
        SceneContext context,
        string compositionFamily,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);

        var tags = window.Items.SelectMany(i => i.Tags).ToList();
        var deterministic = DeterministicClusters(window, tags, out var remaining);

        if (remaining.Count == 0)
        {
            // Speaker-labelled transcripts and screenplays resolve here, with no model call at all:
            // the label IS the surface form and repeat mentions of it are the same referent (§8.4).
            return (
                new CandidateWindow(
                    window.WindowIndex, First(window), Last(window), deterministic, []),
                null);
        }

        var rendered = Render(window, remaining);

        var prompt = new PromptTemplate(runner.Assets.Template("persona-window.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("referent-resolution"))
            .Set("schema", runner.Assets.Schema("persona-window"))
            .Set("familySkill", runner.Assets.FamilySkill(compositionFamily))
            .Set("instincts", context.RenderInstincts(InstinctScope.Persona))
            .Set("window", rendered)
            .Render();

        var issued = remaining.Select(t => t.TagId).ToHashSet(StringComparer.Ordinal);

        var result = await runner.RunOrDegradeAsync<PersonaWindowReply>(
            prompt,
            Routing(context, window, rendered.Length),
            reply => Validate(reply, issued),
            _repair.MaxRoundsPerArtifact,
            ct);

        if (!result.Success)
        {
            logger.LogWarning(
                "Run {RunId}: persona window {Window} fell back to one candidate per surface form ({Check}).",
                context.RunId, window.WindowIndex, result.Validation.CheckId);

            // The degraded answer is "every distinct surface form is its own referent". That
            // over-counts personas and never mis-merges them, which is the right way round: a
            // duplicate entry is visible in review, a false merge is not (§6.1).
            return (
                new CandidateWindow(
                    window.WindowIndex, First(window), Last(window),
                    [.. deterministic, .. PerSurfaceForm(window, remaining, deterministic.Count)], []),
                null);
        }

        var candidates = new List<ReferentCandidate>(deterministic);
        var index = deterministic.Count;

        foreach (var wire in result.Value!.Candidates)
        {
            candidates.Add(new ReferentCandidate(
                CandidateRefs.For(window.WindowIndex, index++),
                ParseKind(wire.Kind),
                wire.SurfaceForm,
                [.. wire.SurfaceForms.Append(wire.SurfaceForm).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
                [.. wire.TagIds.Where(issued.Contains)],
                SectionIdOf(window, wire.TagIds),
                Enum.TryParse<GroupFormation>(wire.Formation, ignoreCase: true, out var formation)
                    ? formation
                    : null,
                MemberRefs: [],
                wire.MembershipComplete,
                wire.Confidence));
        }

        // Member references are resolved against this window's own candidates, in code. The model
        // names members by surface form; turning those into candidate refs is a lookup, and one a
        // model should not be asked to do because it would be inventing references.
        candidates = ResolveMembers(candidates, result.Value.Candidates, deterministic.Count);

        return (
            new CandidateWindow(
                window.WindowIndex,
                First(window),
                Last(window),
                candidates,
                [.. result.Value.OpenQuestions.Where(q => !string.IsNullOrWhiteSpace(q)).Take(MaxOpenQuestions)]),
            result.Response?.Decision.Model.Key);
    }

    /// <summary>
    /// The second lever of §4.5, applied to coreference: speaker labels, registry proper nouns and
    /// repeat mentions of an already-tagged surface form are resolved in code, so the model sees
    /// only novel or ambiguous references.
    /// </summary>
    private static IReadOnlyList<ReferentCandidate> DeterministicClusters(
        PersonaWindow window, IReadOnlyList<Tag> tags, out IReadOnlyList<Tag> remaining)
    {
        var settled = new List<ReferentCandidate>();
        var left = new List<Tag>();
        var index = 0;

        // A speaker label is not a referring expression to be resolved; it is a name, stated. Every
        // item carrying the same label is the same speaker, and that is a fact about the format.
        var byLabel = window.Items
            .Where(i => i.Speech is { SurfaceForm.Length: > 0 })
            .GroupBy(i => i.Speech!.SurfaceForm, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in byLabel)
        {
            var itemIds = group.Select(i => i.ItemId).ToList();
            var groupTags = group.SelectMany(i => i.Tags)
                .Where(t => t.Kind == TagKind.Persona
                    && string.Equals(t.SurfaceForm, group.Key, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.TagId)
                .ToList();

            settled.Add(new ReferentCandidate(
                CandidateRefs.For(window.WindowIndex, index++),
                TagKind.Persona,
                group.Key,
                [group.Key],
                groupTags.Count > 0 ? groupTags : itemIds,
                window.SectionId,
                Formation: null,
                MemberRefs: [],
                MembershipComplete: false,
                Confidence: 1.0));
        }

        var settledForms = settled
            .SelectMany(c => c.SurfaceForms)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            if (settledForms.Contains(tag.SurfaceForm))
            {
                continue;
            }

            left.Add(tag);
        }

        remaining = left;
        return settled;
    }

    /// <summary>The degraded fallback: one candidate per distinct surface form. Over-counts, never merges.</summary>
    private static IReadOnlyList<ReferentCandidate> PerSurfaceForm(
        PersonaWindow window, IReadOnlyList<Tag> tags, int startIndex)
    {
        var index = startIndex;

        return
        [
            .. tags
                .GroupBy(t => (t.Kind, Form: t.SurfaceForm), TagFormComparer.Instance)
                .Select(group => new ReferentCandidate(
                    CandidateRefs.For(window.WindowIndex, index++),
                    group.Key.Kind,
                    group.Key.Form,
                    [group.Key.Form],
                    [.. group.Select(t => t.TagId)],
                    window.SectionId,
                    Formation: null,
                    MemberRefs: [],
                    MembershipComplete: false,
                    Confidence: 0.3)),
        ];
    }

    private static List<ReferentCandidate> ResolveMembers(
        List<ReferentCandidate> candidates, IReadOnlyList<CandidateWire> wires, int offset)
    {
        var byForm = candidates.ToDictionary(c => c.SurfaceForm, c => c.Ref, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < wires.Count; i++)
        {
            if (wires[i].Members.Count == 0)
            {
                continue;
            }

            var position = offset + i;
            if (position >= candidates.Count)
            {
                continue;
            }

            candidates[position] = candidates[position] with
            {
                MemberRefs =
                [
                    .. wires[i].Members
                        .Select(m => byForm.GetValueOrDefault(m))
                        .OfType<string>(),
                ],
            };
        }

        return candidates;
    }

    private static TagKind ParseKind(string kind) => kind.ToLowerInvariant() switch
    {
        "group" => TagKind.Group,
        "place" => TagKind.Place,
        "exhibit-ref" or "exhibitref" or "exhibit" => TagKind.ExhibitRef,
        _ => TagKind.Persona,
    };

    private static string? SectionIdOf(PersonaWindow window, IReadOnlyList<string> tagIds) => window.SectionId;

    private static string First(PersonaWindow window) =>
        window.Paragraphs.Count > 0 ? window.Paragraphs[0].ParagraphId : string.Empty;

    private static string Last(PersonaWindow window) =>
        window.Paragraphs.Count > 0 ? window.Paragraphs[^1].ParagraphId : string.Empty;

    private static ValidationResult Validate(PersonaWindowReply reply, IReadOnlySet<string> issued)
    {
        var errors = new List<string>();
        var claimed = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < reply.Candidates.Count; i++)
        {
            var candidate = reply.Candidates[i];

            if (string.IsNullOrWhiteSpace(candidate.SurfaceForm))
            {
                errors.Add($"candidate {i + 1} has no surface form");
            }

            foreach (var tagId in candidate.TagIds)
            {
                if (!issued.Contains(tagId))
                {
                    errors.Add(
                        $"candidate {i + 1} names tag '{tagId}', which was not offered — tag ids are issued "
                        + "by the pipeline and may not be invented");
                }
                else if (claimed.TryGetValue(tagId, out var first))
                {
                    errors.Add(
                        $"candidate {i + 1} claims tag '{tagId}', already claimed by candidate {first + 1} — "
                        + "a reference resolves to one referent");
                }
                else
                {
                    claimed[tagId] = i;
                }
            }
        }

        return ValidationResult.Fail(Checks.Schema, errors);
    }

    /// <summary>
    /// The window as the windower sees it: every unresolved tag with its offsets and its paragraph's
    /// text, so "she" can be read in the sentence that contains it.
    /// </summary>
    private static string Render(PersonaWindow window, IReadOnlyList<Tag> unresolved)
    {
        var byParagraph = window.Items.ToLookup(i => i.ParagraphId, StringComparer.Ordinal);
        var unresolvedIds = unresolved.Select(t => t.TagId).ToHashSet(StringComparer.Ordinal);
        var builder = new StringBuilder();

        foreach (var paragraph in window.Paragraphs)
        {
            builder.Append("### ").AppendLine(paragraph.ParagraphId);
            builder.AppendLine("```");
            builder.AppendLine(paragraph.Text);
            builder.AppendLine("```");

            var tags = byParagraph[paragraph.ParagraphId]
                .SelectMany(i => i.Tags.Where(t => unresolvedIds.Contains(t.TagId)).Select(t => (i, t)))
                .ToList();

            if (tags.Count > 0)
            {
                builder.AppendLine("references to resolve:");
                foreach (var (item, tag) in tags)
                {
                    builder.Append("- ").Append(tag.TagId)
                        .Append(CultureInfo.InvariantCulture, $" [{tag.Kind}] \"{tag.SurfaceForm}\"")
                        .Append(CultureInfo.InvariantCulture, $" at {tag.StartOffset}..{tag.EndOffset}")
                        .AppendLine(CultureInfo.InvariantCulture, $" in {item.ItemId} ({item.Kind})");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private RoutingContext Routing(SceneContext context, PersonaWindow window, int renderedChars) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.ResolveReferents, AgentRole.PersonaWindower,
            // Mid, for the reason the structure windower is Mid: a slice is a smaller job than a
            // document, and the expensive model is spent once on the join rather than once per part.
            ModelTier.Mid, context.Sensitivity,
            EstimatedInputTokens: 2_000 + renderedChars / 4,
            MaxOutputTokens: Math.Max(4_096, window.Items.Sum(i => i.Tags.Count) * 96))
        {
            PinnedModelKey = context.PinnedKey(AgentRole.PersonaWindower),
            PromptVersion = runner.Assets.VersionFor(AgentRole.PersonaWindower),
            Unit = $"persona-window:{window.WindowIndex}",
            OutputSchema = runner.Assets.WireSchemaElement("persona-window"),
            OutputSchemaName = "persona-window",
        };

    private sealed class TagFormComparer : IEqualityComparer<(TagKind Kind, string Form)>
    {
        public static readonly TagFormComparer Instance = new();

        public bool Equals((TagKind Kind, string Form) x, (TagKind Kind, string Form) y) =>
            x.Kind == y.Kind && string.Equals(x.Form, y.Form, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((TagKind Kind, string Form) obj) =>
            HashCode.Combine(obj.Kind, obj.Form.ToLowerInvariant());
    }
}

/// <summary>One persona-orchestrator turn: what it said, and what the loop should do with it.</summary>
public sealed record RegistryTurn(RegistryPlan? Plan, string Text, string? ModelKey, ValidationResult Validation)
{
    public bool Success => Plan is not null;
}

/// <summary>
/// Phase 9's orchestrator (scene plan §8.7). It sees candidate <b>digests</b> — never tag text and
/// never paragraph text — and emits a registry plan.
///
/// <para>The load-bearing constraint is phase 6's: <b>the orchestrator composes references, not
/// content</b>. It never emits a surface form, a paragraph id or a tag id it was not given; it names
/// candidates the windowers produced. So the same argument holds here as there — adding an agent
/// strengthens <c>text-integrity</c>, because the only ids in play are ones code issued.</para>
/// </summary>
public sealed class PersonaOrchestratorAgent(AgentRunner runner, IOptions<PipelineOptions> options)
{
    private readonly RepairOptions _repair = options.Value.Repair;

    public ChatMessage OpeningMessage(CandidateDigest digest, SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(context);

        return new ChatMessage(
            ChatRole.User,
            new PromptTemplate(runner.Assets.Template("persona-orchestrator.v1"))
                .Set("rules", runner.Assets.Rules)
                .Set("skill", runner.Assets.Skill("referent-orchestration"))
                .Set("schema", runner.Assets.Schema("registry-plan"))
                .Set("familySkill", runner.Assets.FamilySkill(context.CompositionFamily))
                .Set("documentTitle", context.DocumentTitle)
                .Set("digest", RenderDigest(digest))
                .Render());
    }

    public static ChatMessage AnswersMessage(IReadOnlyList<FollowUpAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        if (answers.Count == 0)
        {
            return new ChatMessage(
                ChatRole.User,
                "No window could answer. Build the registry from what the reports already say, "
                + "registering separately anything you cannot confidently merge.");
        }

        var builder = new StringBuilder("## Answers\n");

        foreach (var answer in answers)
        {
            builder.Append("- ").Append(answer.Node).Append(" [").Append(answer.Kind).Append("]: ")
                .Append(answer.Verdict);

            if (!string.IsNullOrWhiteSpace(answer.Evidence))
            {
                builder.Append(" (").Append(answer.Evidence).Append(')');
            }

            builder.AppendLine();
        }

        builder.AppendLine()
            .AppendLine("Return the registry now, or ask again only if an answer left something genuinely open.");

        return new ChatMessage(ChatRole.User, builder.ToString());
    }

    public async Task<RegistryTurn> AdvanceAsync(
        IReadOnlyList<ChatMessage> conversation,
        SceneContext context,
        Func<RegistryPlan, ValidationResult> validate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var result = await runner.RunAsync(
            conversation,
            Routing(context, conversation),
            validate,
            _repair.MaxRoundsPerArtifact,
            ct);

        return new RegistryTurn(
            result.Success ? result.Value : null,
            result.Response?.Text ?? string.Empty,
            result.Response?.Decision.Model.Key,
            result.Validation);
    }

    /// <summary>
    /// Surface forms, counts and first appearance — what a coreference decision reads. No paragraph
    /// ids and no excerpts, so the orchestrator has nothing to copy even if it wanted to.
    /// </summary>
    public static string RenderDigest(CandidateDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var builder = new StringBuilder();

        foreach (var window in digest.Windows)
        {
            builder.Append(CultureInfo.InvariantCulture, $"## Window {window.WindowIndex}")
                .AppendLine(CultureInfo.InvariantCulture, $"  ({window.FirstParagraphId} … {window.LastParagraphId})");

            foreach (var candidate in window.Candidates)
            {
                builder.Append("- ").Append(candidate.Ref)
                    .Append(CultureInfo.InvariantCulture, $"  [{candidate.Kind}] \"{candidate.SurfaceForm}\"");

                if (candidate.SurfaceForms.Count > 1)
                {
                    builder.Append(CultureInfo.InvariantCulture, $"  also: {string.Join(", ", candidate.SurfaceForms.Skip(1))}");
                }

                builder.AppendLine(CultureInfo.InvariantCulture, $"  references={candidate.TagIds.Count}");
            }

            foreach (var question in window.OpenQuestions)
            {
                builder.Append("  ? ").AppendLine(question);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private RoutingContext Routing(SceneContext context, IReadOnlyList<ChatMessage> conversation) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.ResolveReferents,
            AgentRole.PersonaOrchestrator, ModelTier.Large, context.Sensitivity,
            EstimatedInputTokens: 1_000 + conversation.Sum(m => m.Text.Length) / 4,
            MaxOutputTokens: 16_384)
        {
            PinnedModelKey = context.PinnedKey(AgentRole.PersonaOrchestrator),
            PromptVersion = runner.Assets.VersionFor(AgentRole.PersonaOrchestrator),
            Unit = $"persona-orchestrator:{conversation.Count}",
            OutputSchema = runner.Assets.WireSchemaElement("registry-plan"),
            OutputSchemaName = "registry-plan",
        };
}
