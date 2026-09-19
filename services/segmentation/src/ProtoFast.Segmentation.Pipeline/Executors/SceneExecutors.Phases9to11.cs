using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Classification;
using ProtoFast.Segmentation.Core.Families;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Scenes;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline.Agents;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Phase 9 (scene plan §8.7): cluster surface forms into personas and bind every tag to a registry
/// id.
///
/// <para>The one scene phase that is an orchestration, and §8.2's test says why: <b>coreference
/// cannot be made local.</b> No windowing makes "is this the same person" decidable, so there is no
/// invariant a planner could impose and no join code could perform.</para>
///
/// <para>The loop is constructed inside <see cref="PersonaOrchestration"/> rather than held here,
/// for the reason <c>StructureExecutor</c> gives: this executor is a cross-run shared instance whose
/// safety rests on holding no run state, and a conversation is run state.</para>
/// </summary>
public sealed class PersonaExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    PersonaOrchestration orchestration,
    CapabilityGapWriter gapWriter,
    PromptAssets assets,
    SceneContextFactory contexts,
    IOptions<PipelineOptions> options,
    ILogger<PersonaExecutor> logger)
    : Executor<ItemsComplete, ReferentsComplete>(ExecutorIds.Referents, declareCrossRunShareable: true)
{
    public override async ValueTask<ReferentsComplete> HandleAsync(
        ItemsComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Both roles, because both produce the registry. Keying on the windower alone would make a
        // run "already done" after the orchestrator prompt changed.
        var promptVersion = assets.VersionFor(AgentRole.PersonaWindower)
            + "+" + assets.VersionFor(AgentRole.PersonaOrchestrator);

        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.ResolveReferents, promptVersion);

        if (await gate.AlreadyDoneAsync(
                message.RunId, PipelinePhase.ResolveReferents, key, null, cancellationToken) is { } done
            && await artifacts.ReadRegistriesAsync(message.RunId, cancellationToken) is { } cached)
        {
            return new ReferentsComplete(
                message.RunId, done, cached.Registries.Personas.Count, message.ValidationPassed);
        }

        await journal.StartAsync(message.RunId, PipelinePhase.ResolveReferents, key, cancellationToken);

        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(
                PipelinePhase.ResolveReferents, "The assembly artifact is missing.");
        var tree = await artifacts.ReadTreeAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.ResolveReferents, "The tree artifact is missing.");
        var presentation = await artifacts.ReadPresentationAsync(message.RunId, cancellationToken);
        var scopes = await artifacts.ReadFamilyScopesAsync(message.RunId, cancellationToken);
        var items = await artifacts.ReadItemsAsync(message.RunId, cancellationToken);
        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.ResolveReferents, "The run row is missing.");

        var sceneContext = await contexts.CreateAsync(message.RunId, cancellationToken) with
        {
            CompositionFamily = presentation?.CompositionFamily ?? CompositionFamily.Unknown,
        };

        var families = new FamilyResolver(tree.Root, scopes, sceneContext.CompositionFamily);

        // Windowed by leaf section, which is what keeps a window inside one family scope — so no
        // window straddles two families and no join reconciles two policies (§6.1).
        var windows = PlanWindows(tree.Root, assembly.Paragraphs, items);

        var result = await orchestration.RunAsync(
            windows, items, sceneContext, families,
            progress: (note, ct) => journal.NoteAsync(message.RunId, PipelinePhase.ResolveReferents, note, ct),
            cancellationToken);

        await WriteAuditAsync(message.RunId, run, result, key, cancellationToken);

        if (!result.Success)
        {
            // Unlike phase 6 there is no cheaper fallback to drop to — a registry is either built or
            // it is not. The run stops here with an exact error rather than continuing to cut scenes
            // against unresolved referents, which would violate C8 by construction.
            await journal.FailAsync(
                message.RunId, PipelinePhase.ResolveReferents, result.Failure!, cancellationToken);

            throw new PipelineFailureException(PipelinePhase.ResolveReferents, result.Failure!);
        }

        var report = new ValidationReport(
        [
            SceneChecks.CheckTagResolution(result.Items, result.Registries),
            SceneChecks.CheckPersonaScope(result.Registries, scopes),
        ]);

        if (!report.Passed)
        {
            await journal.FailAsync(
                message.RunId, PipelinePhase.ResolveReferents, report.ErrorReport, cancellationToken);

            throw new PipelineFailureException(PipelinePhase.ResolveReferents, report.ErrorReport);
        }

        await artifacts.WriteBoundItemsAsync(message.RunId, result.Items, key, cancellationToken);

        var artifact = await artifacts.WriteRegistriesAsync(
            message.RunId,
            new RegistriesArtifact(
                result.Registries, result.WindowerModelKey, result.OrchestratorModelKey),
            key,
            cancellationToken);

        // Both roles pin per run, so a resumed run does not switch models mid-document
        // [orchestrator §7].
        if (result.WindowerModelKey is { } windowerKey)
        {
            await journal.PinModelAsync(message.RunId, AgentRole.PersonaWindower, windowerKey, cancellationToken);
        }

        if (result.OrchestratorModelKey is { } orchestratorKey)
        {
            await journal.PinModelAsync(
                message.RunId, AgentRole.PersonaOrchestrator, orchestratorKey, cancellationToken);
        }

        logger.LogInformation(
            "Run {RunId}: {Personas} personas, {Places} places, {Exhibits} exhibits across {Windows} windows.",
            message.RunId, result.Registries.Personas.Count, result.Registries.Places.Count,
            result.Registries.Exhibits.Count, windows.Count);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.ResolveReferents, artifact.Key,
            $"{result.Registries.Personas.Count} personas, {result.Registries.Places.Count} places",
            cancellationToken);

        return new ReferentsComplete(
            message.RunId, artifact, result.Registries.Personas.Count, message.ValidationPassed);
    }

    /// <summary>
    /// One window per leaf section, split further where a section is long. Sections rather than a
    /// fixed stride, because a family scope is a section (§6.1) and a window that crossed one would
    /// make the scope unenforceable at exactly the point it matters.
    /// </summary>
    private static IReadOnlyList<PersonaWindow> PlanWindows(
        SectionNode root, IReadOnlyList<Paragraph> paragraphs, IReadOnlyList<SceneItem> items)
    {
        var byId = paragraphs.ToDictionary(p => p.ParagraphId, StringComparer.Ordinal);
        var itemsByParagraph = items.ToLookup(i => i.ParagraphId, StringComparer.Ordinal);
        var windows = new List<PersonaWindow>();

        foreach (var section in root.Descend().Where(n => n.ParagraphIds.Count > 0))
        {
            var sectionParagraphs = section.ParagraphIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();

            if (sectionParagraphs.Count == 0)
            {
                continue;
            }

            foreach (var chunk in sectionParagraphs.Chunk(MaxParagraphsPerWindow))
            {
                windows.Add(new PersonaWindow(
                    windows.Count,
                    chunk,
                    [.. chunk.SelectMany(p => itemsByParagraph[p.ParagraphId])],
                    section.SectionId));
            }
        }

        return windows;
    }

    /// <summary>
    /// Large enough that coreference inside a scene is local, small enough that the window fits a
    /// Mid model's context alongside its items and tags.
    /// </summary>
    private const int MaxParagraphsPerWindow = 24;

    private async Task WriteAuditAsync(
        string runId,
        Data.Entities.Run run,
        PersonaResolutionResult result,
        string key,
        CancellationToken ct)
    {
        if (result.Transcript.Count > 0)
        {
            await artifacts.WritePersonaChatAsync(runId, result.Transcript, key, ct);
        }

        if (result.Plan is { } plan)
        {
            await artifacts.WriteRegistryPlanAsync(runId, plan, key, ct);
        }

        if (result.Gaps.Count > 0 && options.Value.Structure.EmitCapabilityGaps)
        {
            await gapWriter.WriteAsync(runId, run.DocumentFamily, result.Gaps, ct);
        }
    }
}

/// <summary>
/// Phase 10 (scene plan §8.8): assign situations and cut scene boundaries.
///
/// <para>The pleasing case of §8.2: the unit definition's bound on setting inheritance — never
/// across a section boundary [unit §3.1.1] — is exactly what makes the phase windowable with a
/// guaranteed join. A constraint written to cap error propagation turns out to buy reproducibility
/// too.</para>
/// </summary>
public sealed class SceneCutExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    SceneCutterAgent cutter,
    PromptAssets assets,
    SceneContextFactory contexts,
    IOptions<PipelineOptions> options,
    ILogger<SceneCutExecutor> logger)
    : Executor<ReferentsComplete, ScenesComplete>(ExecutorIds.Scenes, declareCrossRunShareable: true)
{
    private readonly SceneCutOptions _scenes = options.Value.Scenes;

    public override async ValueTask<ScenesComplete> HandleAsync(
        ReferentsComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var promptVersion = assets.VersionFor(AgentRole.SceneCutter);
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.CutScenes, promptVersion);

        if (await gate.AlreadyDoneAsync(message.RunId, PipelinePhase.CutScenes, key, null, cancellationToken)
                is { } done
            && await artifacts.ReadScenesAsync(message.RunId, cancellationToken) is { } cached)
        {
            return new ScenesComplete(
                message.RunId, done, cached.Scenes.Count, cached.SuppressedByFloor, message.ValidationPassed);
        }

        await journal.StartAsync(message.RunId, PipelinePhase.CutScenes, key, cancellationToken);

        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.CutScenes, "The assembly artifact is missing.");
        var tree = await artifacts.ReadTreeAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.CutScenes, "The tree artifact is missing.");
        var presentation = await artifacts.ReadPresentationAsync(message.RunId, cancellationToken);
        var scopes = await artifacts.ReadFamilyScopesAsync(message.RunId, cancellationToken);
        var registries = (await artifacts.ReadRegistriesAsync(message.RunId, cancellationToken))?.Registries
            ?? Registries.Empty;
        var items = await artifacts.ReadBoundItemsAsync(message.RunId, cancellationToken);

        var sceneContext = await contexts.CreateAsync(message.RunId, cancellationToken) with
        {
            CompositionFamily = presentation?.CompositionFamily ?? CompositionFamily.Unknown,
        };

        var families = new FamilyResolver(tree.Root, scopes, sceneContext.CompositionFamily);
        var paragraphText = assembly.Paragraphs.ToDictionary(p => p.ParagraphId, p => p.Text, StringComparer.Ordinal);
        var sectionOf = ItemExecutor.SectionOfParagraph(tree.Root);

        var itemsBySection = items
            .GroupBy(i => sectionOf.GetValueOrDefault(i.ParagraphId) ?? tree.Root.SectionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var scenes = new List<Scene>();
        var suppressed = 0;
        string? modelKey = null;

        // Leaf sections in document order. Scenes are the section layer's children, so C12 is a
        // structural fact rather than a check — a scene cannot straddle a boundary when it is a
        // child of one (§2).
        foreach (var section in tree.Root.Descend().Where(n => n.IsLeaf))
        {
            if (!itemsBySection.TryGetValue(section.SectionId, out var sectionItems) || sectionItems.Count == 0)
            {
                continue;
            }

            var family = families.Resolve(section.SectionId);
            var minSceneSpan = _scenes.MinSceneSpanByFamily.TryGetValue(family, out var configured)
                ? configured
                : _scenes.DefaultMinSceneSpan;

            var proposal = SceneCutter.Propose(sectionItems, paragraphText, minSceneSpan);
            suppressed += proposal.SuppressedByFloor;

            var decision = await cutter.CutAsync(
                section.SectionId, sectionItems, paragraphText, proposal, registries,
                sceneContext, family, cancellationToken);

            modelKey ??= decision.ModelKey;

            var materialized = SceneMaterializer.Materialize(
                section.SectionId,
                sectionItems,
                decision.Boundaries,
                proposal.ModeByItem,
                decision.Assignments,
                registries,
                _scenes,
                scenes.Count);

            if (!materialized.Success)
            {
                // Fall back to the deterministic cuts with no assignments. Every coordinate takes
                // its nothing-value, which is legal by C6 and is exactly what K7 promises: the
                // pipeline can be uncertain about a scene without failing the run.
                logger.LogWarning(
                    "Run {RunId}: section {Section} fell back to unassigned scenes — {Report}",
                    message.RunId, section.SectionId, materialized.Validation.ErrorReport);

                materialized = SceneMaterializer.Materialize(
                    section.SectionId, sectionItems, proposal.Boundaries, proposal.ModeByItem,
                    new Dictionary<int, SceneAssignment>(), registries, _scenes, scenes.Count);
            }

            scenes.AddRange(materialized.Scenes);
        }

        // Continues is derived here, in code: it is the C12 split, and "did the situation change"
        // is equality on a tuple (§8.9).
        var linked = ContinuesLinker.Link(scenes, tree.Root);

        var report = new ValidationReport(
        [
            SceneChecks.CheckScenePartition(items, linked.Scenes),
            SceneChecks.CheckTreeLeafScenes(tree.Root, linked.Scenes),
            .. SceneChecks.CheckSituations(linked.Scenes, registries),
            SceneChecks.CheckNoFabrication(linked.Scenes, items, assembly.Paragraphs, registries),
            SceneChecks.CheckGroupClosure(items, registries, linked.Scenes),
        ]);

        if (!report.Passed)
        {
            await journal.FailAsync(message.RunId, PipelinePhase.CutScenes, report.ErrorReport, cancellationToken);
            throw new PipelineFailureException(PipelinePhase.CutScenes, report.ErrorReport);
        }

        foreach (var warning in new[]
                 {
                     SceneChecks.CheckEvidencePresent(linked.Scenes),
                     SceneChecks.CheckSectionContainment(tree.Root, linked.Scenes, items),
                 }.Where(w => !w.Passed))
        {
            await journal.NoteAsync(message.RunId, PipelinePhase.CutScenes, warning.ErrorReport, cancellationToken);
        }

        var artifact = await artifacts.WriteScenesAsync(
            message.RunId,
            new ScenesArtifact(
                linked.Scenes, suppressed, linked.AcrossTrustedHeadings, linked.AcrossInferredBoundaries, modelKey),
            key,
            cancellationToken);

        if (modelKey is not null)
        {
            await journal.PinModelAsync(message.RunId, AgentRole.SceneCutter, modelKey, cancellationToken);
        }

        logger.LogInformation(
            "Run {RunId}: {Scenes} scenes; {Suppressed} candidate cuts suppressed by the MinSceneSpan "
            + "floor; {Trusted} Continues across trusted headings, {Inferred} across inferred boundaries.",
            message.RunId, linked.Scenes.Count, suppressed,
            linked.AcrossTrustedHeadings, linked.AcrossInferredBoundaries);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.CutScenes, artifact.Key,
            $"{linked.Scenes.Count} scenes, {suppressed} cuts suppressed by the floor, "
            + $"{linked.Links.Count} Continues links", cancellationToken);

        return new ScenesComplete(
            message.RunId, artifact, linked.Scenes.Count, suppressed, message.ValidationPassed);
    }
}

/// <summary>
/// Phase 11 (scene plan §8.9): infer the scene links a coordinate cannot derive.
///
/// <para><b>Skippable, and usually skipped.</b> The deterministic pass runs first, and a run whose
/// scenes yield no candidate makes zero model calls. A phase that costs nothing on documents without
/// frames is not overhead — the deterministic skip is doing precisely its job (§13).</para>
/// </summary>
public sealed class SceneLinkExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    SceneLinkOrchestration orchestration,
    CapabilityGapWriter gapWriter,
    PromptAssets assets,
    SceneContextFactory contexts,
    IOptions<PipelineOptions> options,
    ILogger<SceneLinkExecutor> logger)
    : Executor<ScenesComplete, LinksComplete>(ExecutorIds.SceneLinks, declareCrossRunShareable: true)
{
    public override async ValueTask<LinksComplete> HandleAsync(
        ScenesComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var promptVersion = assets.VersionFor(AgentRole.SceneLinkWindower)
            + "+" + assets.VersionFor(AgentRole.SceneLinkOrchestrator);

        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.LinkScenes, promptVersion);

        if (await gate.AlreadyDoneAsync(message.RunId, PipelinePhase.LinkScenes, key, null, cancellationToken)
                is { } done
            && await artifacts.ReadSceneLinksAsync(message.RunId, cancellationToken) is { } cached)
        {
            return new LinksComplete(
                message.RunId, done, cached.Links.Count, cached.ModelCalls, message.ValidationPassed);
        }

        await journal.StartAsync(message.RunId, PipelinePhase.LinkScenes, key, cancellationToken);

        var scenesArtifact = await artifacts.ReadScenesAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.LinkScenes, "The scenes artifact is missing.");
        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.LinkScenes, "The assembly artifact is missing.");
        var presentation = await artifacts.ReadPresentationAsync(message.RunId, cancellationToken);
        var items = await artifacts.ReadBoundItemsAsync(message.RunId, cancellationToken);
        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.LinkScenes, "The run row is missing.");

        var sceneContext = await contexts.CreateAsync(message.RunId, cancellationToken) with
        {
            CompositionFamily = presentation?.CompositionFamily ?? CompositionFamily.Unknown,
        };

        var paragraphText = assembly.Paragraphs.ToDictionary(
            p => p.ParagraphId, p => p.Text, StringComparer.Ordinal);

        var result = await orchestration.RunAsync(
            scenesArtifact.Scenes, items, paragraphText, sceneContext,
            progress: (note, ct) => journal.NoteAsync(message.RunId, PipelinePhase.LinkScenes, note, ct),
            cancellationToken);

        if (result.Transcript.Count > 0)
        {
            await artifacts.WriteSceneLinkChatAsync(message.RunId, result.Transcript, key, cancellationToken);
        }

        if (result.Plan is { } plan)
        {
            await artifacts.WriteSceneLinkPlanAsync(message.RunId, plan, key, cancellationToken);
        }

        if (result.Gaps.Count > 0 && options.Value.Structure.EmitCapabilityGaps)
        {
            await gapWriter.WriteAsync(message.RunId, run.DocumentFamily, result.Gaps, cancellationToken);
        }

        if (!result.Success)
        {
            // K4 is worth less than the document. A link plan that will not materialize costs the
            // inferred links and leaves the deterministic Continues ones in place.
            logger.LogWarning(
                "Run {RunId}: phase 11 produced no link plan ({Reason}); the deterministic links stand.",
                message.RunId, result.Failure);

            await journal.NoteAsync(
                message.RunId, PipelinePhase.LinkScenes, "link plan not applied: " + result.Failure,
                cancellationToken);
        }

        var scenes = result.Success ? result.Scenes : scenesArtifact.Scenes;

        var report = new ValidationReport(
        [
            SceneChecks.CheckLinkIntegrity(scenes),
            SceneChecks.CheckLinkEvidence(scenes),
        ]);

        if (!report.Passed)
        {
            await journal.FailAsync(message.RunId, PipelinePhase.LinkScenes, report.ErrorReport, cancellationToken);
            throw new PipelineFailureException(PipelinePhase.LinkScenes, report.ErrorReport);
        }

        // The scenes are rewritten with their links, so a reader of 10_scenes.json sees the finished
        // scene rather than half of one. The links artifact stays as the phase's own record.
        await artifacts.WriteScenesAsync(
            message.RunId, scenesArtifact with { Scenes = scenes }, key, cancellationToken);

        var links = scenes.SelectMany(s => s.Links).Distinct().ToList();

        var artifact = await artifacts.WriteSceneLinksAsync(
            message.RunId,
            new SceneLinksArtifact(
                links, result.ModelCalls, result.WindowerModelKey, result.OrchestratorModelKey),
            key,
            cancellationToken);

        if (result.WindowerModelKey is { } windowerKey)
        {
            await journal.PinModelAsync(
                message.RunId, AgentRole.SceneLinkWindower, windowerKey, cancellationToken);
        }

        if (result.OrchestratorModelKey is { } orchestratorKey)
        {
            await journal.PinModelAsync(
                message.RunId, AgentRole.SceneLinkOrchestrator, orchestratorKey, cancellationToken);
        }

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.LinkScenes, artifact.Key,
            result.ModelCalls == 0
                ? $"{links.Count} links, no model calls — the deterministic pass found no candidates"
                : $"{links.Count} links from {result.ModelCalls} model calls",
            cancellationToken);

        return new LinksComplete(
            message.RunId, artifact, links.Count, result.ModelCalls, message.ValidationPassed);
    }
}
