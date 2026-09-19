using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Scenes;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>The link windower's wire format.</summary>
public sealed record SceneLinkWindowReply
{
    [JsonPropertyName("links")]
    public IReadOnlyList<LinkProposal> Links { get; init; } = [];

    [JsonPropertyName("openQuestions")]
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
}

/// <summary>
/// Proposes links inside a window of scene <b>digests</b> (scene plan §8.9).
///
/// <para>Windows are runs of consecutive scenes with overlap, and the windower's
/// <c>openQuestions</c> carry exactly the candidate whose other endpoint is out of window —
/// "this scene resumes something; its frame is before my first scene." That is the case the
/// orchestrator exists to settle, and there is no windowing that removes it.</para>
/// </summary>
public sealed class SceneLinkWindowerAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<SceneLinkWindowerAgent> logger)
{
    private readonly RepairOptions _repair = options.Value.Repair;

    public const int MaxOpenQuestions = 5;

    public async Task<SceneLinkWindowResult> ProposeAsync(
        SceneLinkWindow window,
        SceneContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);

        var rendered = RenderDigests(window.Digests);

        var prompt = new PromptTemplate(runner.Assets.Template("scene-link-window.v1"))
            .Set("rules", runner.Assets.Rules)
            .Set("skill", runner.Assets.Skill("scene-linking"))
            .Set("schema", runner.Assets.Schema("scene-links"))
            .Set("familySkill", runner.Assets.FamilySkill(context.CompositionFamily))
            .Set("window", rendered)
            .Render();

        var sceneIds = window.Digests.Select(d => d.SceneId).ToHashSet(StringComparer.Ordinal);

        var result = await runner.RunOrDegradeAsync<SceneLinkWindowReply>(
            prompt,
            Routing(context, window, rendered.Length),
            reply => Validate(reply.Links, sceneIds),
            _repair.MaxRoundsPerArtifact,
            ct);

        if (!result.Success)
        {
            // A window that could not propose links costs links, never scenes. K4 is an addition to
            // the scene record, not a part of it.
            logger.LogWarning(
                "Run {RunId}: scene-link window {Window} proposed nothing ({Check}).",
                context.RunId, window.WindowIndex, result.Validation.CheckId);

            return new SceneLinkWindowResult(window.WindowIndex, [], [], null);
        }

        return new SceneLinkWindowResult(
            window.WindowIndex,
            // Only links whose FROM endpoint this window commits. Overlap is context; a window that
            // kept a link about a context scene would duplicate the window before it.
            [.. result.Value!.Links.Where(l => Commits(window, l.From))],
            [.. result.Value.OpenQuestions.Where(q => !string.IsNullOrWhiteSpace(q)).Take(MaxOpenQuestions)],
            result.Response?.Decision.Model.Key);
    }

    private static bool Commits(SceneLinkWindow window, string sceneId) =>
        window.Digests.FirstOrDefault(d => d.SceneId == sceneId) is { } digest && window.Commits(digest.Ordinal);

    internal static ValidationResult Validate(IReadOnlyList<LinkProposal> links, IReadOnlySet<string> sceneIds)
    {
        var errors = new List<string>();

        foreach (var link in links)
        {
            if (!sceneIds.Contains(link.From))
            {
                errors.Add($"'{link.From}' is not a scene in this window");
            }

            if (!sceneIds.Contains(link.To))
            {
                errors.Add($"'{link.To}' is not a scene in this window");
            }

            if (!Enum.TryParse<SceneLinkKind>(link.Kind, ignoreCase: true, out var kind))
            {
                errors.Add($"'{link.Kind}' is not one of the six link kinds");
            }
            else if (kind == SceneLinkKind.Continues)
            {
                errors.Add(
                    "Continues is derived in code at the cut, not proposed — it is the C12 split, and a "
                    + "model proposing one is describing a boundary rather than a relation");
            }

            if (link.Evidence.Count == 0)
            {
                errors.Add(
                    $"the {link.Kind} from '{link.From}' to '{link.To}' cites nothing. Every link names the "
                    + "ids justifying it (C13); an uncited link is not stored.");
            }
        }

        return ValidationResult.Fail(Checks.Schema, errors);
    }

    /// <summary>
    /// Id, title, mode, Time, cast ids, place id, subject and the Transition span — and nothing
    /// else. A thousand-scene novel presents as a digest list, not as a document.
    /// </summary>
    internal static string RenderDigests(IReadOnlyList<SceneDigest> digests)
    {
        var builder = new StringBuilder();

        foreach (var digest in digests)
        {
            builder.Append(digest.SceneId)
                .Append(CultureInfo.InvariantCulture, $"  #{digest.Ordinal}")
                .Append("  mode=").Append(digest.Mode)
                .Append("  time=").Append(digest.Time.Relation);

            if (digest.Time.Anchor is { Length: > 0 } anchor)
            {
                builder.Append(CultureInfo.InvariantCulture, $"({anchor})");
            }

            builder.Append("  place=").Append(digest.PlaceId ?? "void")
                .Append("  cast=").Append(digest.CastPersonaIds.Count == 0
                    ? "none"
                    : string.Join('+', digest.CastPersonaIds));

            if (digest.Subject is { Length: > 0 })
            {
                builder.Append("  subject=\"").Append(digest.Subject).Append('"');
            }

            if (digest.TransitionText is { Length: > 0 } transition)
            {
                builder.Append("  transition=\"").Append(transition).Append('"');
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private RoutingContext Routing(SceneContext context, SceneLinkWindow window, int renderedChars) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.LinkScenes, AgentRole.SceneLinkWindower,
            ModelTier.Mid, context.Sensitivity,
            EstimatedInputTokens: 1_500 + renderedChars / 4,
            MaxOutputTokens: Math.Max(2_048, window.Digests.Count * 96))
        {
            PinnedModelKey = context.PinnedKey(AgentRole.SceneLinkWindower),
            PromptVersion = runner.Assets.VersionFor(AgentRole.SceneLinkWindower),
            Unit = $"scene-link-window:{window.WindowIndex}",
            OutputSchema = runner.Assets.WireSchemaElement("scene-links"),
            OutputSchemaName = "scene-links",
        };
}

/// <summary>One link-orchestrator turn.</summary>
public sealed record SceneLinkTurn(SceneLinkPlan? Plan, string Text, string? ModelKey, ValidationResult Validation)
{
    public bool Success => Plan is not null;
}

/// <summary>
/// Phase 11's orchestrator (scene plan §8.9). The second and last place in the plan that meets
/// §8.2's test: <c>FlashbackOf</c>, <c>ConcurrentWith</c> and <c>Frames</c> relate scenes that may
/// be a hundred apart, and <b>no window planner can put both endpoints in one window</b>.
///
/// <para>The cheaper alternative — a step inside review — does not work: review runs in fresh
/// context by design, and a reviewer that must also hold the whole scene sequence in order to find
/// a frame has stopped being a reviewer.</para>
/// </summary>
public sealed class SceneLinkOrchestratorAgent(AgentRunner runner, IOptions<PipelineOptions> options)
{
    private readonly RepairOptions _repair = options.Value.Repair;

    public ChatMessage OpeningMessage(
        IReadOnlyList<SceneDigest> digests, SceneLinkDigest proposals, SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(digests);
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(context);

        return new ChatMessage(
            ChatRole.User,
            new PromptTemplate(runner.Assets.Template("scene-link-orchestrator.v1"))
                .Set("rules", runner.Assets.Rules)
                .Set("skill", runner.Assets.Skill("scene-link-orchestration"))
                .Set("schema", runner.Assets.Schema("scene-link-plan"))
                .Set("familySkill", runner.Assets.FamilySkill(context.CompositionFamily))
                .Set("documentTitle", context.DocumentTitle)
                .Set("scenes", SceneLinkWindowerAgent.RenderDigests(digests))
                .Set("proposals", RenderProposals(proposals))
                .Render());
    }

    public async Task<SceneLinkTurn> AdvanceAsync(
        IReadOnlyList<ChatMessage> conversation,
        SceneContext context,
        Func<SceneLinkPlan, ValidationResult> validate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var result = await runner.RunAsync(
            conversation, Routing(context, conversation), validate, _repair.MaxRoundsPerArtifact, ct);

        return new SceneLinkTurn(
            result.Success ? result.Value : null,
            result.Response?.Text ?? string.Empty,
            result.Response?.Decision.Model.Key,
            result.Validation);
    }

    internal static string RenderProposals(SceneLinkDigest digest)
    {
        var builder = new StringBuilder();

        foreach (var window in digest.Windows)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"## Window {window.WindowIndex}");

            foreach (var link in window.Links)
            {
                builder.Append("- ").Append(link.Kind).Append(": ").Append(link.From)
                    .Append(" → ").Append(link.To)
                    .AppendLine(CultureInfo.InvariantCulture, $"  evidence={string.Join(",", link.Evidence)}");
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
            context.RunId, context.DocumentId, PipelinePhase.LinkScenes,
            AgentRole.SceneLinkOrchestrator, ModelTier.Large, context.Sensitivity,
            EstimatedInputTokens: 1_000 + conversation.Sum(m => m.Text.Length) / 4,
            MaxOutputTokens: 8_192)
        {
            PinnedModelKey = context.PinnedKey(AgentRole.SceneLinkOrchestrator),
            PromptVersion = runner.Assets.VersionFor(AgentRole.SceneLinkOrchestrator),
            Unit = $"scene-link-orchestrator:{conversation.Count}",
            OutputSchema = runner.Assets.WireSchemaElement("scene-link-plan"),
            OutputSchemaName = "scene-link-plan",
        };
}
