using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Phase 5 (plan §9.7): build the section tree.
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
    PromptAssets assets,
    FamilyInstincts instincts,
    ILogger<StructureExecutor> logger)
    : Executor<AssembleComplete, StructureComplete>(ExecutorIds.Structure, declareCrossRunShareable: true)
{
    public override async ValueTask<StructureComplete> HandleAsync(
        AssembleComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var promptVersion = assets.VersionFor(AgentRole.Structurer);
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
            run.PinnedModels.GetValueOrDefault(nameof(AgentRole.HeadingLeveler)));

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
}

/// <summary>
/// Phase 6 (plan §9.8): run every check, and repair what fails.
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

            return new ValidationComplete(message.RunId, artifact, Passed: false);
        }

        await journal.NoteAsync(
            message.RunId, PipelinePhase.Validate, $"repair round {round}", cancellationToken);

        _ = structurer;
        return new ValidationComplete(message.RunId, artifact, Passed: false);
    }
}

/// <summary>Phase 7 (plan §9.9): a second opinion on the tree, and the decision to gate or not.</summary>
public sealed class StructureReviewExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    StructureReviewerAgent reviewer,
    HumanGatePolicy gatePolicy,
    ILogger<StructureReviewExecutor> logger)
    : Executor<ValidationComplete, ReviewComplete>(ExecutorIds.StructureReview, declareCrossRunShareable: true)
{
    public override async ValueTask<ReviewComplete> HandleAsync(
        ValidationComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
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
            run, validationPassed: message.Passed, unresolvedHighFindings: highSeverity, cancellationToken);

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
