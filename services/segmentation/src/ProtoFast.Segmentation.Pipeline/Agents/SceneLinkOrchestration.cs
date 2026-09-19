using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Scenes;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline.Executors;
using ProtoFast.Segmentation.Routing;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>What phase 11 produced. <see cref="ModelCalls"/> is zero on a document with no candidates.</summary>
public sealed record SceneLinkResult(
    IReadOnlyList<Scene> Scenes,
    IReadOnlyList<SceneLink> Links,
    SceneLinkPlan? Plan,
    IReadOnlyList<TranscriptEntry> Transcript,
    IReadOnlyList<CapabilityGapProposal> Gaps,
    int ModelCalls,
    string? WindowerModelKey,
    string? OrchestratorModelKey,
    string? Failure)
{
    public bool Success => Failure is null;
}

/// <summary>The bench of link windowers — <c>WindowBench</c>'s shape with its own directive type.</summary>
public sealed class SceneLinkBench(
    SceneLinkWindowerAgent windower,
    RunArtifacts artifacts,
    PhaseGate gate,
    PromptAssets assets,
    IOptions<PipelineOptions> options,
    ILogger<SceneLinkBench> logger)
{
    private readonly SceneLinkOptions _links = options.Value.SceneLinks;

    public async Task<(SceneLinkDigest Digest, int Calls, string? ModelKey)> ProposeAllAsync(
        IReadOnlyList<SceneLinkWindow> windows,
        SceneContext context,
        Func<string, CancellationToken, Task>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(context);

        var promptVersion = assets.VersionFor(AgentRole.SceneLinkWindower);
        var results = new Dictionary<int, SceneLinkWindowResult>();
        var calls = 0;
        string? modelKey = null;

        foreach (var batch in windows.Chunk(Math.Max(1, _links.FanOutBatchSize)))
        {
            var tasks = batch.Select(window => ProposeOneAsync(window, context, promptVersion, ct));

            foreach (var (result, called) in await Task.WhenAll(tasks))
            {
                results[result.WindowIndex] = result;
                calls += called ? 1 : 0;
                modelKey ??= result.ModelKey;
            }

            if (progress is not null)
            {
                await progress($"{results.Count} of {windows.Count} scene-link windows done", ct);
            }
        }

        return (
            new SceneLinkDigest([.. results.OrderBy(kv => kv.Key).Select(kv => kv.Value)]),
            calls,
            modelKey);
    }

    private async Task<(SceneLinkWindowResult Result, bool Called)> ProposeOneAsync(
        SceneLinkWindow window, SceneContext context, string promptVersion, CancellationToken ct)
    {
        var idempotencyKey = IdempotencyKeys.SceneLinkWindow(context.RunId, window.WindowIndex, promptVersion);
        var artifactKey = ArtifactKeys.SceneLinkWindow(context.RunId, window.WindowIndex);

        if (await gate.AlreadyDoneAsync(artifactKey, idempotencyKey, ct) is not null
            && await artifacts.ReadSceneLinkWindowAsync(context.RunId, window.WindowIndex, ct) is { } cached)
        {
            logger.LogDebug("Run {RunId}: reusing scene-link window {Window}", context.RunId, window.WindowIndex);
            return (cached, false);
        }

        var result = await windower.ProposeAsync(window, context, ct);
        await artifacts.WriteSceneLinkWindowAsync(context.RunId, result, idempotencyKey, ct);

        return (result, true);
    }
}

/// <summary>
/// Phase 11's loop (scene plan §8.9). Phase 9's machinery part for part, which is why a separate
/// phase is affordable at all: it costs a directive type and a materializer, not an architecture.
///
/// <para><b>It is skippable, and usually skipped.</b> The deterministic pass runs first, and a run
/// whose scenes yield no candidate makes zero model calls — the same way a clean heading hierarchy
/// skips the structure agents. Most textbooks and most transcripts have no frames and no
/// flashbacks and pay nothing for the phase.</para>
/// </summary>
public sealed class SceneLinkOrchestration(
    SceneLinkBench bench,
    SceneLinkOrchestratorAgent orchestrator,
    IOptions<PipelineOptions> options,
    ILogger<SceneLinkOrchestration> logger)
{
    private readonly SceneLinkOptions _links = options.Value.SceneLinks;

    public async Task<SceneLinkResult> RunAsync(
        IReadOnlyList<Scene> scenes,
        IReadOnlyList<SceneItem> items,
        IReadOnlyDictionary<string, string> paragraphText,
        SceneContext context,
        Func<string, CancellationToken, Task>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);

        var digests = SceneDigests.Build(scenes, items, paragraphText);
        var candidates = SceneDigests.Candidates(digests);

        if (candidates.Count == 0)
        {
            logger.LogInformation(
                "Run {RunId}: no flashback, frame or concurrency candidate among {Scenes} scenes; "
                + "phase 11 makes no model calls.",
                context.RunId, scenes.Count);

            return new SceneLinkResult(
                scenes, [.. scenes.SelectMany(s => s.Links)], null, [], [], 0, null, null, null);
        }

        var windows = SceneDigests.Plan(digests, _links);
        var (digest, calls, windowerKey) = await bench.ProposeAllAsync(windows, context, progress, ct);

        // One window means every endpoint the bench could see was in range, so there is nothing for
        // the orchestrator to settle. Materializing the bench's own proposals is the whole job.
        if (windows.Count <= 1 || !digest.Windows.Any(w => w.OpenQuestions.Count > 0))
        {
            var direct = new SceneLinkPlan { Links = [.. digest.AllLinks] };
            var applied = SceneLinkMaterializer.Apply(direct, scenes, _links);

            return applied.Success
                ? new SceneLinkResult(
                    applied.Scenes, applied.Links, direct, [], [], calls, windowerKey, null, null)
                : new SceneLinkResult(
                    scenes, [], direct, [], [], calls, windowerKey, null,
                    "the bench's links did not materialize: " + applied.Validation.ErrorReport);
        }

        var transcript = new List<TranscriptEntry>
        {
            new(0, "bench", SceneLinkOrchestratorAgent.RenderProposals(digest)),
        };

        var gaps = new List<CapabilityGapProposal>();
        var conversation = new List<ChatMessage> { orchestrator.OpeningMessage(digests, digest, context) };
        var rounds = Math.Max(1, _links.MaxOrchestratorRounds);

        for (var round = 0; round < rounds; round++)
        {
            SceneLinkTurn turn;

            try
            {
                turn = await orchestrator.AdvanceAsync(
                    conversation, context, plan => Validate(plan, scenes), ct);
            }
            catch (NoEligibleModelException exception)
            {
                // The deterministic Continues links stand and the inferred ones are simply absent,
                // which is what a document with no frames looks like anyway. K4 is an addition to
                // the scene record, not a part of it.
                logger.LogInformation(
                    "Run {RunId}: no link orchestrator is available ({Reason}); the deterministic links "
                    + "stand.",
                    context.RunId, exception.Message);

                return new SceneLinkResult(
                    scenes, [.. scenes.SelectMany(s => s.Links)], null, transcript, gaps, calls,
                    windowerKey, null, null);
            }

            calls++;
            transcript.Add(new TranscriptEntry(round + 1, "orchestrator", turn.Text));

            if (!turn.Success)
            {
                // A failed link plan is not a failed run: the scenes keep their deterministic
                // Continues links and lose only the inferred ones. K4 is worth less than C11, and
                // by the same argument it is worth less than the document.
                logger.LogWarning(
                    "Run {RunId}: the link orchestrator produced nothing usable ({Error}); the scenes keep "
                    + "their deterministic links.",
                    context.RunId, turn.Validation.CheckId);

                return new SceneLinkResult(
                    scenes, [.. scenes.SelectMany(s => s.Links)], null, transcript, gaps, calls,
                    windowerKey, turn.ModelKey, null);
            }

            var plan = turn.Plan!;
            gaps.AddRange(plan.Gaps);

            if (plan.IsComplete)
            {
                var applied = SceneLinkMaterializer.Apply(plan, scenes, _links);

                return applied.Success
                    ? new SceneLinkResult(
                        applied.Scenes, applied.Links, plan, transcript, gaps, calls,
                        windowerKey, turn.ModelKey, null)
                    : new SceneLinkResult(
                        scenes, [], plan, transcript, gaps, calls, windowerKey, turn.ModelKey,
                        "the validated plan did not materialize: " + applied.Validation.ErrorReport);
            }

            conversation =
            [
                .. conversation,
                new ChatMessage(ChatRole.Assistant, turn.Text),
                new ChatMessage(
                    ChatRole.User,
                    "No window can answer a question about a scene outside its own range — that is why "
                    + "you have the whole digest list. Return the link plan now, and an empty list if the "
                    + "document has no frames and no flashbacks."),
            ];
        }

        return new SceneLinkResult(
            scenes, [.. scenes.SelectMany(s => s.Links)], null, transcript, gaps, calls,
            windowerKey, null, null);
    }

    private ValidationResult Validate(SceneLinkPlan plan, IReadOnlyList<Scene> scenes)
    {
        if (plan.IsComplete)
        {
            return SceneLinkMaterializer.Apply(plan, scenes, _links).Validation;
        }

        var known = scenes.Select(s => s.SceneId).ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var followUp in plan.FollowUps)
        {
            if (!SceneLinkFollowUpKinds.IsKnown(followUp.Kind))
            {
                errors.Add($"'{followUp.Kind}' is not one of the three questions that can be asked");
            }

            if (!known.Contains(followUp.Node))
            {
                errors.Add($"'{followUp.Node}' is not a scene of this run");
            }
        }

        if (errors.Count == 0 && plan.FollowUps.Count == 0)
        {
            errors.Add("the reply has neither links nor a follow-up question");
        }

        return ValidationResult.Fail(SceneLinkMaterializer.LinkPlanCheck, errors);
    }
}
