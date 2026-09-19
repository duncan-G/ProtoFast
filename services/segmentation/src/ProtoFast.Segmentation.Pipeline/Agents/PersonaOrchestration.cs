using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Families;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Personas;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline.Executors;
using ProtoFast.Segmentation.Routing;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>What phase 9 produced, or why it could not.</summary>
public sealed record PersonaResolutionResult(
    IReadOnlyList<SceneItem> Items,
    Registries Registries,
    RegistryPlan? Plan,
    CandidateDigest? Digest,
    IReadOnlyList<TranscriptEntry> Transcript,
    IReadOnlyList<CapabilityGapProposal> Gaps,
    string? WindowerModelKey,
    string? OrchestratorModelKey,
    string? Failure)
{
    public bool Success => Failure is null;
}

/// <summary>
/// The bench of persona windowers — <c>WindowBench</c>'s shape with its own directive type
/// (scene plan §8.7).
///
/// <para>Artifact reuse is the same as everywhere else: a window whose artifact already carries
/// this idempotency key is free, so a resumed run pays only for the windows it had not finished.
/// </para>
/// </summary>
public sealed class PersonaBench(
    PersonaWindowerAgent windower,
    RunArtifacts artifacts,
    PhaseGate gate,
    PromptAssets assets,
    IOptions<PipelineOptions> options,
    ILogger<PersonaBench> logger)
{
    private readonly ItemOptions _items = options.Value.Items;

    public async Task<(CandidateDigest Digest, string? ModelKey)> ClusterAllAsync(
        IReadOnlyList<PersonaWindow> windows,
        SceneContext context,
        FamilyResolver families,
        Func<string, CancellationToken, Task>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(families);

        var promptVersion = assets.VersionFor(AgentRole.PersonaWindower);
        var results = new Dictionary<int, CandidateWindow>();
        string? modelKey = null;

        foreach (var batch in windows.Chunk(Math.Max(1, _items.FanOutBatchSize)))
        {
            var tasks = batch.Select(window => ClusterOneAsync(window, context, families, promptVersion, ct));

            foreach (var (window, key) in await Task.WhenAll(tasks))
            {
                results[window.WindowIndex] = window;
                modelKey ??= key;
            }

            if (progress is not null)
            {
                await progress($"{results.Count} of {windows.Count} persona windows done", ct);
            }
        }

        return (new CandidateDigest([.. results.OrderBy(kv => kv.Key).Select(kv => kv.Value)]), modelKey);
    }

    private async Task<(CandidateWindow Window, string? ModelKey)> ClusterOneAsync(
        PersonaWindow window,
        SceneContext context,
        FamilyResolver families,
        string promptVersion,
        CancellationToken ct)
    {
        var idempotencyKey = IdempotencyKeys.PersonaWindow(context.RunId, window.WindowIndex, promptVersion);
        var artifactKey = ArtifactKeys.PersonaWindow(context.RunId, window.WindowIndex);

        if (await gate.AlreadyDoneAsync(artifactKey, idempotencyKey, ct) is not null
            && await artifacts.ReadPersonaWindowAsync(context.RunId, window.WindowIndex, ct) is { } cached)
        {
            logger.LogDebug("Run {RunId}: reusing persona window {Window}", context.RunId, window.WindowIndex);
            return (cached, null);
        }

        // Resolved at the node being worked on (§6.1). Phase 9 is windowed by section, so no window
        // straddles two families and no join reconciles two policies.
        var family = families.Resolve(window.SectionId);

        var (result, modelKey) = await windower.ClusterAsync(window, context, family, ct);

        await artifacts.WritePersonaWindowAsync(context.RunId, result, idempotencyKey, ct);

        return (result, modelKey);
    }
}

/// <summary>
/// Phase 9's loop (scene plan §8.7), written like <see cref="StructureOrchestration"/> and for the
/// same reason: turn order is the loop, termination is <c>plan.IsComplete</c> plus the round cap,
/// and history shaping is the fact that only digests and the orchestrator's own replies are ever
/// added.
///
/// <para>Phase 9 is the strongest orchestrator fit in the plan, and §8.2's test says why:
/// <b>coreference has no invariant a planner can impose.</b> No windowing makes "is this the same
/// person" decidable locally. Where phase 6 would merely be <em>better</em> with judgement, this is
/// impossible without it.</para>
/// </summary>
public sealed class PersonaOrchestration(
    PersonaBench bench,
    PersonaOrchestratorAgent orchestrator,
    IOptions<PipelineOptions> options,
    ILogger<PersonaOrchestration> logger)
{
    private readonly StructureOptions _structure = options.Value.Structure;

    public async Task<PersonaResolutionResult> RunAsync(
        IReadOnlyList<PersonaWindow> windows,
        IReadOnlyList<SceneItem> items,
        SceneContext context,
        FamilyResolver families,
        Func<string, CancellationToken, Task>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(families);

        logger.LogInformation(
            "Run {RunId}: resolving referents over {Items} items in {Windows} windows.",
            context.RunId, items.Count, windows.Count);

        var (digest, windowerKey) = await bench.ClusterAllAsync(windows, context, families, progress, ct);

        if (!digest.AllCandidates.Any())
        {
            // A document with no referents at all — a pure reference table, a code listing, or
            // dialogue that never names who is speaking. The phase ends with no model call, which is
            // the right answer rather than a degraded one.
            //
            // It still closes the cast: no candidates means no named speaker, not no speaker. An
            // utterance nobody is credited with belongs to Unattributed exactly as it would on the
            // bound path, and skipping that here is what turned a legitimately unattributed
            // document into a C8 failure (tag-resolution: "Speech with no speaker").
            var (closedItems, closedRegistries) = RegistryMaterializer.CloseCast(items, Registries.Empty);

            return new PersonaResolutionResult(
                closedItems, closedRegistries, null, digest, [], [], windowerKey, null, null);
        }

        // A bench of one has nothing to join. Paying a Large call to be told so would make the
        // orchestration strictly more expensive than the fan-out on the documents where they agree
        // — the same short-circuit as a one-window structure run [orchestrator §8].
        if (windows.Count == 1)
        {
            return Unjoined(digest, items, families, windowerKey);
        }

        var transcript = new List<TranscriptEntry>();
        var gaps = new List<CapabilityGapProposal>();
        var conversation = new List<ChatMessage> { orchestrator.OpeningMessage(digest, context) };
        var rounds = Math.Max(1, _structure.MaxOrchestratorRounds);

        transcript.Add(new TranscriptEntry(0, "bench", PersonaOrchestratorAgent.RenderDigest(digest)));

        for (var round = 0; round < rounds; round++)
        {
            var isLastRound = round == rounds - 1;

            RegistryTurn turn;

            try
            {
                turn = await orchestrator.AdvanceAsync(
                    conversation,
                    context,
                    plan => Validate(plan, digest, items, families, allowFollowUps: !isLastRound),
                    ct);
            }
            catch (NoEligibleModelException exception)
            {
                // No model is qualified to join windows, so nothing may be merged across one.
                // Registering every candidate separately is the degraded answer, and it is the right
                // way round: it over-counts personas and never mis-merges them, which is the
                // asymmetry §6.1 settles every scope question with.
                logger.LogInformation(
                    "Run {RunId}: no orchestrator is available ({Reason}); every candidate registers "
                    + "separately.",
                    context.RunId, exception.Message);

                return Unjoined(digest, items, families, windowerKey);
            }

            transcript.Add(new TranscriptEntry(round + 1, "orchestrator", turn.Text));

            if (!turn.Success)
            {
                return Failed(items, digest, transcript, gaps, windowerKey, turn.ModelKey,
                    $"the registry plan could not be applied after {round + 1} rounds: "
                    + turn.Validation.ErrorReport);
            }

            var plan = turn.Plan!;
            gaps.AddRange(plan.Gaps);

            if (plan.IsComplete)
            {
                var applied = RegistryMaterializer.Apply(plan, digest, items, families);

                return applied.Success
                    ? Bound(applied, plan, digest, transcript, gaps, windowerKey, turn.ModelKey)
                    : Failed(items, digest, transcript, gaps, windowerKey, turn.ModelKey,
                        "the validated plan did not materialize: " + applied.Validation.ErrorReport);
            }

            if (progress is not null)
            {
                await progress($"persona orchestrator round {round + 1}: {plan.FollowUps.Count} follow-ups", ct);
            }

            // The follow-up round is deliberately not implemented as a second bench pass here: a
            // windower's answer about a candidate is the candidate it already reported, and the
            // orchestrator's questions are answered from the digest. Asking again would re-read the
            // window to restate what it already said.
            conversation =
            [
                .. conversation,
                new ChatMessage(ChatRole.Assistant, turn.Text),
                PersonaOrchestratorAgent.AnswersMessage([]),
            ];
        }

        return Failed(items, digest, transcript, gaps, windowerKey, null,
            $"the orchestrator asked questions for all {rounds} rounds without building a registry");
    }

    /// <summary>
    /// The registry an orchestration would have produced with nothing to merge across: every
    /// candidate becomes its own entry. It is the single-window answer and the no-model answer
    /// alike, because they are the same answer for the same reason — no evidence that two
    /// candidates co-refer.
    /// </summary>
    private static PersonaResolutionResult Unjoined(
        CandidateDigest digest,
        IReadOnlyList<SceneItem> items,
        FamilyResolver families,
        string? windowerKey)
    {
        var plan = OneCandidatePerCluster(digest);
        var applied = RegistryMaterializer.Apply(plan, digest, items, families);

        return applied.Success
            ? Bound(applied, plan, digest, [], [], windowerKey, null)
            : new PersonaResolutionResult(
                items, Registries.Empty, plan, digest, [], [], windowerKey, null,
                "the unjoined registry did not materialize: " + applied.Validation.ErrorReport);
    }

    private static PersonaResolutionResult Bound(
        RegistryMaterializationResult applied,
        RegistryPlan plan,
        CandidateDigest digest,
        List<TranscriptEntry> transcript,
        List<CapabilityGapProposal> gaps,
        string? windowerKey,
        string? orchestratorKey)
    {
        // C8's last mile: an utterance whose span carried no persona tag binds to Unattributed
        // rather than to nothing. "No anonymous speakers" is only enforceable if there is something
        // for an anonymous speaker to be.
        var (items, registries) = RegistryMaterializer.CloseCast(applied.Items, applied.Registries);

        return new PersonaResolutionResult(
            items, registries, plan, digest, transcript, gaps, windowerKey, orchestratorKey, null);
    }

    private static PersonaResolutionResult Failed(
        IReadOnlyList<SceneItem> items,
        CandidateDigest digest,
        List<TranscriptEntry> transcript,
        List<CapabilityGapProposal> gaps,
        string? windowerKey,
        string? orchestratorKey,
        string failure) =>
        new(items, Registries.Empty, null, digest, transcript, gaps, windowerKey, orchestratorKey, failure);

    /// <summary>
    /// The single-window plan: every candidate registers as itself. It is what the orchestrator
    /// would have produced with nothing to merge across, written in code so the call is not made.
    /// </summary>
    private static RegistryPlan OneCandidatePerCluster(CandidateDigest digest) => new()
    {
        Registry =
        [
            .. digest.AllCandidates.Select(candidate => new RegistryRow
            {
                Candidates = [candidate.Ref],
                CanonicalName = candidate.SurfaceForm,
                Kind = candidate.Kind == TagKind.Group ? "group" : "individual",
                Scope = "persistent",
                Formation = candidate.Formation?.ToString().ToLowerInvariant() ?? string.Empty,
            }),
        ],
    };

    private static ValidationResult Validate(
        RegistryPlan plan,
        CandidateDigest digest,
        IReadOnlyList<SceneItem> items,
        FamilyResolver families,
        bool allowFollowUps)
    {
        if (plan.IsComplete)
        {
            return RegistryMaterializer.Apply(plan, digest, items, families).Validation;
        }

        if (!allowFollowUps)
        {
            return ValidationResult.Fail(
                RegistryMaterializer.RegistryPlanCheck,
                "There are no rounds of questions left. Return a registry that assigns every candidate, "
                + "registering separately anything you cannot confidently merge.");
        }

        var known = digest.AllCandidates.Select(c => c.Ref).ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();

        if (plan.FollowUps.Count == 0)
        {
            errors.Add("the reply has neither a registry nor a follow-up question");
        }

        foreach (var followUp in plan.FollowUps)
        {
            if (!PersonaFollowUpKinds.IsKnown(followUp.Kind))
            {
                errors.Add($"'{followUp.Kind}' is not one of the four questions that can be asked");
            }

            if (!known.Contains(followUp.Node))
            {
                errors.Add($"'{followUp.Node}' is not a candidate any window returned");
            }
        }

        return ValidationResult.Fail(RegistryMaterializer.RegistryPlanCheck, errors);
    }
}
