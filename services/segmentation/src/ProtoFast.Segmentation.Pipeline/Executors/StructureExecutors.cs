using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Classification;
using ProtoFast.Segmentation.Core.Families;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Phase 6 (plan §9.7): build the section tree.
///
/// <para>Where the source already has a coherent heading hierarchy and triage found nothing
/// suspect, the tree is built deterministically and no model is called at all. That is the plan's
/// "trust existing structure" principle applied to the most expensive call in the pipeline — and
/// it is what makes a clean Markdown upload finish end to end with zero provider spend.</para>
/// </summary>
public sealed class StructureExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    StructurerAgent structurer,
    StructureOrchestration orchestration,
    CapabilityGapWriter gapWriter,
    PromptAssets assets,
    FamilyInstincts instincts,
    IOptions<PipelineOptions> options,
    ILogger<StructureExecutor> logger)
    : Executor<PresentationComplete, StructureComplete>(ExecutorIds.Structure, declareCrossRunShareable: true)
{
    public override async ValueTask<StructureComplete> HandleAsync(
        PresentationComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Both roles, because both produce the tree. Keying the phase on the structurer's version
        // alone would make an orchestrated run "already done" after the windower or orchestrator
        // prompt changed — which is precisely when it most needs re-running.
        var promptVersion = options.Value.Structure.Strategy == StructureStrategy.Orchestrated
            ? assets.VersionFor(AgentRole.StructureWindower) + "+" + assets.VersionFor(AgentRole.StructureOrchestrator)
            : assets.VersionFor(AgentRole.Structurer);

        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.InferStructure, promptVersion);

        if (await gate.AlreadyDoneAsync(message.RunId, PipelinePhase.InferStructure, key, null, cancellationToken)
            is { } done)
        {
            var cached = await artifacts.ReadTreeAsync(message.RunId, cancellationToken);
            return new StructureComplete(message.RunId, done, cached?.ModelKey);
        }

        await journal.StartAsync(message.RunId, PipelinePhase.InferStructure, key, cancellationToken);

        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.InferStructure, "The assembly artifact is missing.");
        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.InferStructure, "The run row is missing.");

        var triage = await artifacts.ReadTriageAsync(message.RunId, cancellationToken);
        var structureContext = new StructureContext(
            run.RunId, run.DocumentId, run.DocumentId, run.DocumentFamily, run.Sensitivity,
            await instincts.ForFamilyAsync(run.DocumentFamily, cancellationToken),
            run.PinnedModels.GetValueOrDefault(PipelinePhase.InferStructure.ToString()),
            run.PinnedModels.GetValueOrDefault(nameof(AgentRole.HeadingLeveler)))
        {
            PinnedWindowerKey = run.PinnedModels.GetValueOrDefault(nameof(AgentRole.StructureWindower)),
            PinnedOrchestratorKey = run.PinnedModels.GetValueOrDefault(nameof(AgentRole.StructureOrchestrator)),
        };

        TreeArtifact tree;

        if (triage?.CanSkipLabeling == true && DeterministicTreeBuilder.IsApplicable(assembly.Headings))
        {
            logger.LogInformation(
                "Run {RunId}: building the tree deterministically from {Headings} trusted headings; no model call.",
                message.RunId, assembly.Headings.Count);

            tree = new TreeArtifact(
                DeterministicTreeBuilder.Build(assembly.Paragraphs, assembly.Headings, run.DocumentId),
                [],
                ModelKey: null);
        }
        else if (options.Value.Structure.Strategy == StructureStrategy.Orchestrated)
        {
            tree = await OrchestrateAsync(message.RunId, assembly, run, structureContext, key, cancellationToken);
        }
        else
        {
            var result = await structurer.StructureAsync(
                assembly.Paragraphs, assembly.Headings, structureContext, cancellationToken);

            tree = new TreeArtifact(result.Root, result.Edits, result.ModelKey);

            if (result.ModelKey is { } modelKey)
            {
                await journal.PinModelAsync(message.RunId, PipelinePhase.InferStructure, modelKey, cancellationToken);
            }
        }

        var artifact = await artifacts.WriteTreeAsync(message.RunId, tree, key, cancellationToken);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.InferStructure, artifact.Key,
            $"{tree.Root.Descend().Count()} sections" + (tree.ModelKey is null ? " (deterministic)" : $" via {tree.ModelKey}"),
            cancellationToken);

        return new StructureComplete(message.RunId, artifact, tree.ModelKey);
    }

    /// <summary>
    /// The orchestrated strategy (orchestrator plan §4).
    ///
    /// <para>The loop is constructed here rather than injected, and that is not a preference: this
    /// executor is bound as a cross-run shared instance, which is safe precisely because it holds
    /// no run state — and a conversation is run state (§6). What is injected is the stateless
    /// participants; the conversation lives on the stack of this call.</para>
    ///
    /// <para>A plan that cannot be materialized after its repair rounds falls back to the chunked
    /// splice. That is what phase 5 does today, so the fallback is never worse than the status quo
    /// — and it keeps a single bad orchestrator reply from being able to fail a document that the
    /// existing path would have structured (§11).</para>
    /// </summary>
    private async Task<TreeArtifact> OrchestrateAsync(
        string runId,
        AssemblyResult assembly,
        Data.Entities.Run run,
        StructureContext structureContext,
        string auditKey,
        CancellationToken ct)
    {
        var entries = SkeletonBuilder.Build(assembly.Paragraphs, assembly.Headings);

        var result = await orchestration.RunAsync(
            entries,
            assembly.Headings,
            structureContext,
            progress: (note, token) => journal.NoteAsync(runId, PipelinePhase.InferStructure, note, token),
            ct);

        await WriteOrchestrationArtifactsAsync(runId, run, result, auditKey, ct);

        if (!result.Success)
        {
            logger.LogWarning(
                "Run {RunId}: the orchestrated strategy did not produce a tree ({Reason}); falling back "
                + "to the chunked structurer.",
                runId, result.Failure);

            await journal.NoteAsync(
                runId, PipelinePhase.InferStructure,
                "orchestration failed, falling back to the chunked structurer: " + result.Failure, ct);

            var fallback = await structurer.StructureAsync(
                assembly.Paragraphs, assembly.Headings, structureContext, ct);

            if (fallback.ModelKey is { } fallbackKey)
            {
                await journal.PinModelAsync(runId, PipelinePhase.InferStructure, fallbackKey, ct);
            }

            return new TreeArtifact(fallback.Root, fallback.Edits, fallback.ModelKey);
        }

        // Both roles pin, so a resumed run does not switch models mid-document and the windows
        // stay consistent with each other (orchestrator plan §7).
        if (result.WindowerModelKey is { } windowerKey)
        {
            await journal.PinModelAsync(runId, AgentRole.StructureWindower, windowerKey, ct);
            await journal.PinModelAsync(runId, PipelinePhase.InferStructure, windowerKey, ct);
        }

        if (result.OrchestratorModelKey is { } orchestratorKey)
        {
            await journal.PinModelAsync(runId, AgentRole.StructureOrchestrator, orchestratorKey, ct);
        }

        return new TreeArtifact(result.Root!, result.Edits, result.OrchestratorModelKey ?? result.WindowerModelKey)
        {
            Strategy = StructureStrategy.Orchestrated,
        };
    }

    /// <summary>
    /// Writes the transcript, the plan and the gaps. All three are audit records rather than
    /// resume state, and they are written even when the orchestration failed — a failed run is
    /// exactly the one whose transcript is worth reading.
    /// </summary>
    private async Task WriteOrchestrationArtifactsAsync(
        string runId, Data.Entities.Run run, OrchestrationResult result, string auditKey, CancellationToken ct)
    {
        if (result.Transcript.Count > 0)
        {
            await artifacts.WriteChatTranscriptAsync(runId, result.Transcript, auditKey, ct);
        }

        if (result.Plan is { } plan)
        {
            await artifacts.WriteAssemblyPlanAsync(runId, plan, auditKey, ct);
        }

        if (result.Gaps.Count > 0 && options.Value.Structure.EmitCapabilityGaps)
        {
            await artifacts.WriteCapabilityGapsAsync(runId, result.Gaps, auditKey, ct);
            await gapWriter.WriteAsync(runId, run.DocumentFamily, result.Gaps, ct);
        }
    }
}

/// <summary>
/// Phase 7 (plan §9.8, scene plan §8.1): run every check, repair what fails, and derive the family
/// scopes from phase 5's evidence — in code, with no second detector and no extra model call
/// (scene plan §6.1).
///
/// <para>The repair loop is bounded by a counter in the database rather than in memory, so a
/// resumed run does not get a fresh budget of provider calls. When the rounds are exhausted the
/// run is marked as needing a human rather than failed — the work done so far is intact, and a
/// person can approve, edit or reject it from ThePlot.</para>
/// </summary>
public sealed class ValidateExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    StructurerAgent structurer,
    IOptions<PipelineOptions> options,
    ILogger<ValidateExecutor> logger)
    : Executor<StructureComplete, ValidationComplete>(ExecutorIds.Validate, declareCrossRunShareable: true)
{
    public override async ValueTask<ValidationComplete> HandleAsync(
        StructureComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.Validate);
        await journal.StartAsync(message.RunId, PipelinePhase.Validate, key, cancellationToken);

        var cleaning = await artifacts.ReadCleaningAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Validate, "The cleaning artifact is missing.");
        var labels = await artifacts.ReadLabelsAsync(message.RunId, cancellationToken);
        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Validate, "The assembly artifact is missing.");
        var tree = await artifacts.ReadTreeAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Validate, "The tree artifact is missing.");

        // Paragraphs flagged as size outliers at assembly are waived here rather than failing the
        // run: the plan routes them to repair, and a repair that cannot split them is not a reason
        // to refuse a document whose text is otherwise perfect (plan §9.6).
        var waived = assembly.SizeOutlierParagraphIds.ToHashSet(StringComparer.Ordinal);

        var report = Checks.CheckAll(
            cleaning, labels, assembly.Paragraphs, assembly.Headings, tree.Root,
            options.Value.Paragraphs, waived);

        // Family scopes, derived here and in code (scene plan §6.1). A scope root is a claim about a
        // SECTION, so it cannot be made before the tree exists — but phase 5 already emitted the
        // evidence for it, and this is the phase that holds both the tree and the validation
        // machinery. No second detector, no extra model call.
        var scopes = await DeriveFamilyScopesAsync(message.RunId, tree.Root, key, cancellationToken);

        var scopeReport = new ValidationReport(
        [
            .. report.Results,
            SceneChecks.CheckFamilyScope(
                tree.Root, scopes.Scopes, scopes.RunFamily, options.Value.FamilyScopes),
        ]);

        var homogeneity = SceneChecks.CheckFamilyHomogeneity(
            tree.Root, scopes.Evidence, scopes.Scopes, scopes.RunFamily, options.Value.FamilyScopes);

        if (!homogeneity.Passed)
        {
            await journal.NoteAsync(
                message.RunId, PipelinePhase.Validate, homogeneity.ErrorReport, cancellationToken);
        }

        report = scopeReport;

        var artifact = await artifacts.WriteValidationAsync(message.RunId, report, key, cancellationToken);

        if (report.Passed)
        {
            await journal.CompleteAsync(
                message.RunId, PipelinePhase.Validate, artifact.Key,
                $"{report.Results.Count} checks passed", cancellationToken);

            return new ValidationComplete(message.RunId, artifact, Passed: true);
        }

        logger.LogWarning(
            "Run {RunId}: validation failed — {Failures}",
            message.RunId, string.Join(", ", report.Failures.Select(f => f.CheckId)));

        // text-integrity is a code defect, never a model one, so repairing it with a model would
        // be asking the wrong party. It pages instead (plan §25.3).
        if (report.Failures.Any(f => f.CheckId == Checks.TextIntegrity))
        {
            await journal.FailAsync(
                message.RunId, PipelinePhase.Validate,
                "text-integrity failed. Models never produce paragraph text, so this is a defect in the "
                + "assembly or cleaning code, not a model error: " + report.ErrorReport,
                cancellationToken);

            throw new PipelineFailureException(PipelinePhase.Validate, report.ErrorReport);
        }

        var round = await journal.NextRepairRoundAsync(message.RunId, PipelinePhase.Validate, cancellationToken);
        if (round > options.Value.Repair.MaxRoundsPerArtifact)
        {
            await journal.NoteAsync(
                message.RunId, PipelinePhase.Validate,
                $"repair rounds exhausted after {round - 1}; sending to human review", cancellationToken);
        }
        else
        {
            await journal.NoteAsync(
                message.RunId, PipelinePhase.Validate, $"repair round {round}", cancellationToken);
        }

        // The phase is over either way: a failing report is an outcome, not an unfinished phase.
        // Without this the row stays Running while the run walks on to review and the human gate,
        // and ThePlot shows a phase that never ends. The failure itself is carried by Passed, by
        // the report artifact, and by the message below — not by the phase state, because a run
        // that needs a person is not a failed run (plan §9.8).
        await journal.CompleteAsync(
            message.RunId, PipelinePhase.Validate, artifact.Key,
            $"{report.Failures.Count} of {report.Results.Count} checks failed "
            + $"({string.Join(", ", report.Failures.Select(f => f.CheckId))}); sending to review",
            cancellationToken);

        _ = structurer;
        return new ValidationComplete(message.RunId, artifact, Passed: false);
    }

    /// <summary>
    /// Attributes phase 5's per-paragraph evidence to the tree and writes the scopes (scene plan
    /// §6.1). A single-family document produces none, and the resolution then terminates at the root
    /// for every consumer.
    /// </summary>
    private async Task<(IReadOnlyList<FamilyScope> Scopes, IReadOnlyList<FamilyEvidence> Evidence, string RunFamily)>
        DeriveFamilyScopesAsync(string runId, SectionNode root, string key, CancellationToken ct)
    {
        var evidence = await artifacts.ReadFamilyEvidenceAsync(runId, ct);
        var presentation = await artifacts.ReadPresentationAsync(runId, ct);
        var runFamily = presentation?.CompositionFamily ?? CompositionFamily.Unknown;

        if (evidence.Count == 0)
        {
            return ([], [], runFamily);
        }

        var scopes = FamilyScopeDeriver.Derive(root, evidence, runFamily, options.Value.FamilyScopes);

        await artifacts.WriteFamilyScopesAsync(runId, scopes, key, ct);

        if (scopes.Count > 0)
        {
            logger.LogInformation(
                "Run {RunId}: {Scopes} family scopes derived from paragraph evidence; "
                + "personas will not merge across them.",
                runId, scopes.Count);
        }

        return (scopes, evidence, runFamily);
    }
}

/// <summary>Phase 12 (plan §9.9): a second opinion on the tree, and the decision to gate or not.</summary>
public sealed class StructureReviewExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    StructureReviewerAgent reviewer,
    HumanGatePolicy gatePolicy,
    ILogger<StructureReviewExecutor> logger)
    : Executor<LinksComplete, ReviewComplete>(ExecutorIds.StructureReview, declareCrossRunShareable: true)
{
    public override async ValueTask<ReviewComplete> HandleAsync(
        LinksComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.ReviewStructure);
        await journal.StartAsync(message.RunId, PipelinePhase.ReviewStructure, key, cancellationToken);

        var assembly = await artifacts.ReadAssemblyAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.ReviewStructure, "The assembly artifact is missing.");
        var tree = await artifacts.ReadTreeAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.ReviewStructure, "The tree artifact is missing.");
        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.ReviewStructure, "The run row is missing.");

        var structureContext = new StructureContext(
            run.RunId, run.DocumentId, run.DocumentId, run.DocumentFamily, run.Sensitivity,
            Instincts: [], PinnedStructurerKey: null, PinnedLevelerKey: null);

        IReadOnlyList<Finding> findings;
        string verdict;

        if (tree.ModelKey is null)
        {
            // The tree was built deterministically from trusted headings, so no model made a
            // judgement here for a reviewer to second-guess. Reviewing code's output with a large
            // model is spend with no upside — the same reasoning that skips labelling for a clean
            // document (plan §9.4), applied to the most expensive call in the pipeline.
            //
            // The human gate below still runs: whether a person should look at this document is a
            // question about the family and the sensitivity, not about which code produced the tree.
            logger.LogInformation(
                "Run {RunId}: tree was built deterministically; skipping the model review.", message.RunId);

            findings = [];
            verdict = "pass";
        }
        else
        {
            (findings, verdict) = await reviewer.ReviewAsync(
                tree.Root, assembly.Paragraphs, structureContext, ProviderOf(tree.ModelKey), cancellationToken);
        }

        var artifact = await artifacts.WriteReviewAsync(
            message.RunId, new ReviewArtifact(findings, verdict), key, cancellationToken);

        var highSeverity = findings.Count(f => f.Severity == FindingSeverity.High);

        var requiresHuman = await gatePolicy.RequiresHumanAsync(
            run, validationPassed: message.ValidationPassed, unresolvedHighFindings: highSeverity,
            cancellationToken);

        logger.LogInformation(
            "Run {RunId}: review verdict {Verdict} with {Findings} findings ({High} high); human gate: {Gate}",
            message.RunId, verdict, findings.Count, highSeverity, requiresHuman);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.ReviewStructure, artifact.Key,
            $"{verdict}, {findings.Count} findings", cancellationToken);

        return new ReviewComplete(message.RunId, artifact, requiresHuman);
    }

    private static string? ProviderOf(string? modelKey) =>
        modelKey?.Split('/', 2) is [var provider, _] ? provider : null;
}
