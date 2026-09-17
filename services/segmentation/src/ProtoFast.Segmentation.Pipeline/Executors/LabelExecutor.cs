using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Windowing;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Phase 3 (plan §9.5): plan the windows, label them in parallel, merge, then assign heading
/// levels in one sequential pass (phase 3b).
///
/// <para>The fan-out lives <em>inside</em> this executor rather than across MAF edges. The plan
/// calls for a planner that "loops in batches rather than fanning out at once" (§13.2), and the
/// number of windows is not known until the document has been triaged — so there is no fixed set
/// of edges to build. Batching here gives the same bounded concurrency, with a progress note and
/// a checkpoint after every batch.</para>
/// </summary>
public sealed class LabelExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    LabelerAgent labeler,
    HeadingLevelAgent leveler,
    PromptAssets assets,
    FamilyInstincts instincts,
    IOptions<PipelineOptions> options,
    ILogger<LabelExecutor> logger)
    : Executor<TriageComplete, LabelsMerged>(ExecutorIds.Label, declareCrossRunShareable: true)
{
    /// <summary>
    /// Windows labelled concurrently per batch. Bounded because a 400-page document produces
    /// hundreds of windows, and releasing them all at once would pile up against the provider's
    /// concurrency limit faster than the budget ledger can shape the load (plan §13.2).
    /// </summary>
    private const int BatchSize = 50;

    public override async ValueTask<LabelsMerged> HandleAsync(
        TriageComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var promptVersion = assets.VersionFor(AgentRole.Labeler);
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.Label, promptVersion);
        var artifactKey = ArtifactKeys.Phase(message.RunId, PipelinePhase.Label);

        if (await gate.AlreadyDoneAsync(message.RunId, PipelinePhase.Label, key, null, cancellationToken) is { } done)
        {
            logger.LogDebug("Run {RunId}: labels already complete; reusing {Key}", message.RunId, done.Key);
            return new LabelsMerged(message.RunId, done);
        }

        var cleaning = await artifacts.ReadCleaningAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Label, "The cleaning artifact is missing.");

        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Label, "The run row is missing.");

        var windowResults = new Dictionary<int, IReadOnlyList<LineLabelResult>>();
        IReadOnlyList<LabelWindow> windows = [];

        if (!message.NeedsLabeling)
        {
            // The cheap path (plan §9.4). The merge still runs — it is what turns trusted
            // boundaries and cleaning continuations into one label per line — but no model is
            // called, and most clean Markdown uploads take this route.
            await journal.SkipAsync(message.RunId, PipelinePhase.Label, "no suspect regions", cancellationToken);
        }
        else
        {
            await journal.StartAsync(message.RunId, PipelinePhase.Label, key, cancellationToken);

            var triage = await artifacts.ReadTriageAsync(message.RunId, cancellationToken)
                ?? throw new PipelineFailureException(PipelinePhase.Label, "The triage artifact is missing.");
            var statistics = await artifacts.ReadStatisticsAsync(message.RunId, cancellationToken);

            var trusted = cleaning.Boundaries.Select(b => b.BeforeLineId).ToHashSet(StringComparer.Ordinal);
            windows = new WindowPlanner(options.Value).PlanAll(cleaning.ContentLines, triage.SuspectRegions, trusted);

            logger.LogInformation(
                "Run {RunId}: labelling {Windows} windows over {Regions} suspect regions",
                message.RunId, windows.Count, triage.SuspectRegions.Count);

            var labelContext = new LabelingContext(
                run.RunId, run.DocumentId, run.DocumentFamily, run.Sensitivity, statistics,
                await instincts.ForFamilyAsync(run.DocumentFamily, cancellationToken),
                RunningOutline: [],
                PreviousLabels: [],
                PinnedModelKey: run.PinnedModels.GetValueOrDefault(PipelinePhase.Label.ToString()));

            windowResults = await LabelWindowsAsync(windows, labelContext, promptVersion, cancellationToken);
        }

        var merged = LabelMerger.Merge(cleaning.ContentLines, cleaning, windowResults, windows);
        merged = await AssignHeadingLevelsAsync(merged, cleaning.ContentLines, run, cancellationToken);

        var artifact = await artifacts.WriteLabelsAsync(message.RunId, merged, key, cancellationToken);

        if (message.NeedsLabeling)
        {
            await journal.CompleteAsync(
                message.RunId, PipelinePhase.Label, artifact.Key,
                $"{windows.Count} windows labelled", cancellationToken);
        }

        _ = artifactKey;
        return new LabelsMerged(message.RunId, artifact);
    }

    /// <summary>
    /// Labels the windows in bounded batches, reusing any window whose artifact already carries
    /// this idempotency key. A resumed run therefore pays only for the windows it had not finished
    /// — which is what makes a mid-run deploy cheap as well as safe.
    /// </summary>
    private async Task<Dictionary<int, IReadOnlyList<LineLabelResult>>> LabelWindowsAsync(
        IReadOnlyList<LabelWindow> windows,
        LabelingContext labelContext,
        string promptVersion,
        CancellationToken ct)
    {
        var results = new Dictionary<int, IReadOnlyList<LineLabelResult>>();

        foreach (var batch in windows.Chunk(BatchSize))
        {
            var tasks = batch.Select(async window =>
            {
                var windowKey = IdempotencyKeys.Window(labelContext.RunId, window.WindowIndex, promptVersion);
                var artifactKey = ArtifactKeys.LabelWindow(labelContext.RunId, window.WindowIndex);

                if (await gate.AlreadyDoneAsync(artifactKey, windowKey, ct) is not null
                    && await artifacts.ReadWindowLabelsAsync(labelContext.RunId, window.WindowIndex, ct) is { } cached)
                {
                    return (window.WindowIndex, Labels: cached);
                }

                var labels = await labeler.LabelAsync(window, labelContext, ct);
                await artifacts.WriteWindowLabelsAsync(labelContext.RunId, window.WindowIndex, labels, windowKey, ct);
                return (window.WindowIndex, Labels: labels);
            });

            foreach (var (index, labels) in await Task.WhenAll(tasks))
            {
                results[index] = labels;
            }

            await journal.NoteAsync(
                labelContext.RunId, PipelinePhase.Label,
                $"{results.Count} of {windows.Count} windows labelled", ct);
        }

        return results;
    }

    /// <summary>
    /// Phase 3b (plan §10.1): one sequential pass over the headings the merge produced.
    ///
    /// <para>It has to be separate from labelling because a level is a claim about the whole
    /// document — window 4 cannot know whether its heading is a peer of window 17's. Its input is
    /// just the headings, so one call covers a few hundred of them and the sequential dependency
    /// costs almost nothing. The assigned levels are written back onto the labels, which is where
    /// the assembler reads them.</para>
    /// </summary>
    private async Task<IReadOnlyList<LineLabelResult>> AssignHeadingLevelsAsync(
        IReadOnlyList<LineLabelResult> labels,
        IReadOnlyList<LineRecord> lines,
        Data.Entities.Run run,
        CancellationToken ct)
    {
        var byId = lines.ToDictionary(l => l.LineId, StringComparer.Ordinal);

        var headings = labels
            .Where(l => l.Label == LineLabel.Head)
            .Select(l => byId.TryGetValue(l.LineId, out var line)
                ? new HeadingRecord(l.LineId, line.Text, l.HeadingLevel ?? line.HeadingLevelHint, l.Confidence, BoundarySource.Llm, line.Layout)
                : null)
            .Where(h => h is not null)
            .Select(h => h!)
            .ToList();

        if (headings.Count == 0)
        {
            return labels;
        }

        var assigned = await leveler.AssignAsync(
            headings,
            new StructureContext(
                run.RunId, run.DocumentId, run.DocumentId, run.DocumentFamily, run.Sensitivity,
                Instincts: [],
                PinnedStructurerKey: null,
                PinnedLevelerKey: run.PinnedModels.GetValueOrDefault(nameof(AgentRole.HeadingLeveler))),
            ct);

        var levels = assigned.ToDictionary(h => h.LineId, h => h.Level ?? 1, StringComparer.Ordinal);

        return
        [
            .. labels.Select(l => l.Label == LineLabel.Head && levels.TryGetValue(l.LineId, out var level)
                ? l with { HeadingLevel = level }
                : l),
        ];
    }
}
