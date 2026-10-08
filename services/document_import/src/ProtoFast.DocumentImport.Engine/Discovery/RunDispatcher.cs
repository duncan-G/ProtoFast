using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Scheduling;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Discovery;

/// <summary>Hands each run to exactly one owner: the discovery agent or the scheduler.</summary>
public sealed class RunDispatcher(
    DocumentSignatures documentSignatures,
    IDocumentFamilyPolicyStore families,
    IRegistry registry,
    Scheduler scheduler,
    IDiscoveryAgent agent,
    AgentToolsFactory tools,
    IRunLedger ledger,
    IWorkflowMiner miner,
    IMinedWorkflowStore drafts,
    IOutcomeQueue outcomes,
    IShadowSampler sampler,
    EngineOptions options,
    TimeProvider time,
    ILogger<RunDispatcher> logger)
{
    /// <param name="input">Already stored under <see cref="ArtifactRef.InputStageId"/>.</param>
    public async Task<RunSummary> RunAsync(ArtifactRef input, CancellationToken ct)
    {
        var lastRunId = (await ledger.ProgressAsync([input.RunId], ct)).GetValueOrDefault(input.RunId)?.RunId;

        // What failed after the run is the caller's to retry; the run's output still stands.
        if (await FinishedRunAsync(lastRunId, input, ct) is { } finished)
        {
            logger.LogInformation(
                "Run {RunId} already finished source {SourceId}; this attempt reuses it", finished.RunId, input.RunId);
            return finished;
        }

        // The classifier is a model, so a redelivery that classified afresh could leave the
        // source's open run stranded under a family it never had.
        var open = await OpenRunAsync(lastRunId, ct);
        var documentSignature = open?.DocumentSignature ?? await documentSignatures.ClassifyAsync(input, ct);
        var family = await families.GetAsync(documentSignature.Family, ct);
        var workflow = family.Workflow is { } promoted && await registry.IsPromotedAsync(promoted, ct)
            ? await registry.ResolveAsync(promoted, ct)
            : null;

        if (family.Mode == RunMode.Scheduled && workflow is not null)
        {
            return await RunScheduledAsync(workflow, input, documentSignature, reportsProgress: true, ct);
        }

        var shadow = workflow is not null && sampler.Take(options.Thresholds.ShadowSampleRate)
            ? QuietAsync(RunScheduledAsync(workflow, input, documentSignature, reportsProgress: false, ct))
            : Task.CompletedTask;

        RunSummary summary;
        try
        {
            summary = await RunDiscoveryAsync(input, documentSignature, open, ct);
        }
        finally
        {
            await shadow;
        }

        await MineIfDueAsync(documentSignature.Family, ct);
        return summary;
    }

    private async Task<RunSummary> RunDiscoveryAsync(
        ArtifactRef input, DocumentSignature documentSignature, RunSummary? resumed, CancellationToken ct)
    {
        var runId = resumed?.RunId ?? DocumentImportIds.New();
        var trace = new TraceRef(runId);
        using var activity = StartRun(RunMode.Discovery, documentSignature);
        activity?.SetTag("document_import.run_id", runId);
        activity?.SetTag("document_import.resumed", resumed is not null);

        try
        {
            if (resumed is null)
            {
                await ledger.OpenAsync(runId, documentSignature, RunMode.Discovery, ct);
            }
            else
            {
                logger.LogInformation(
                    "Resuming discovery run {RunId} for source {SourceId} with {Stages} stage attempts recorded",
                    runId, input.RunId, resumed.Stages.Count);
            }

            await ledger.ReportAsync(input.RunId, new RunProgress(RunPhase.Running, runId), ct);
            await agent.RunAsync(input, tools.ForRun(runId, documentSignature, input, trace, ct), trace, ct);

            // Only finished runs are closed, so only finished runs are mined.
            await ledger.CloseAsync(runId, trace, ct);
            return await ledger.SummariseAsync(runId, ct);
        }
        catch (DiscoveryFailedException e)
        {
            // The run, not the infrastructure, failed: a redelivery must not resume it.
            activity.Fail(e);
            await ledger.AbandonAsync(runId, e.Message, CancellationToken.None);
            throw;
        }
        catch (Exception e)
        {
            activity.Fail(e);
            throw;
        }
    }

    /// <summary>The source's last run, when it closed having read this very input.</summary>
    private async Task<RunSummary?> FinishedRunAsync(string? runId, ArtifactRef input, CancellationToken ct)
    {
        if (runId is null)
        {
            return null;
        }

        var finished = await ledger.FindClosedAsync(runId, ct);
        return finished is not null && finished.Stages.Any(s => s.Inputs.Contains(input)) ? finished : null;
    }

    /// <summary>The source's last run, when it is a discovery run still open.</summary>
    private async Task<RunSummary?> OpenRunAsync(string? runId, CancellationToken ct)
    {
        if (runId is null)
        {
            return null;
        }

        var open = await ledger.FindOpenAsync(runId, ct);
        return open is { Mode: RunMode.Discovery } ? open : null;
    }

    private async Task<RunSummary> RunScheduledAsync(
        WorkflowDefinition workflow, ArtifactRef input, DocumentSignature documentSignature, bool reportsProgress, CancellationToken ct)
    {
        using var activity = StartRun(RunMode.Scheduled, documentSignature);
        activity?.SetTag("document_import.workflow", $"{workflow.Ref.Id}@{workflow.Ref.Version}");

        RunSummary summary;
        try
        {
            summary = await scheduler.RunAsync(workflow, input, documentSignature, reportsProgress, ct);
        }
        catch (Exception e)
        {
            activity.Fail(e);
            if (e is StageFailedException)
            {
                await PublishOutcomeAsync(documentSignature.Family, workflow, passed: false, degraded: false, ct);
            }

            throw;
        }

        activity?.SetTag("document_import.run_id", summary.RunId);

        // Reaching here means every stage passed.
        var terminal = workflow.TerminalStages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var degraded = summary.Stages
            .Where(s => terminal.Contains(s.StageId) && s is { IsShadow: false, Passed: true })
            .GroupBy(s => s.StageId)
            .Any(g => g.Last().Degraded);

        await PublishOutcomeAsync(documentSignature.Family, workflow, passed: true, degraded, ct);
        return summary;
    }

    private Task PublishOutcomeAsync(
        string family, WorkflowDefinition workflow, bool passed, bool degraded, CancellationToken ct) =>
        outcomes.PublishAsync(Outcome.ForWorkflow(family, workflow.Ref, passed, degraded, time.GetUtcNow()), ct);

    private static Activity? StartRun(RunMode mode, DocumentSignature documentSignature)
    {
        var activity = DocumentImportTelemetry.Source.StartActivity($"{mode.ToString().ToLowerInvariant()} run");
        activity?.SetTag("document_import.mode", mode.ToString());
        activity?.SetTag("document_import.family", documentSignature.Family);
        return activity;
    }

    private async Task MineIfDueAsync(string family, CancellationToken ct)
    {
        var every = options.Thresholds.MineAfterRuns;
        if (every <= 0 || await ledger.CountAsync(family, RunMode.Discovery, ct) % every != 0)
        {
            return;
        }

        try
        {
            var runs = await ledger.RecentAsync(family, RunMode.Discovery, every, ct);
            if (await miner.MineAsync(family, runs, ct) is not { } mined)
            {
                return;
            }

            StageGraph.Validate(mined.Workflow);
            var published = await registry.PublishAsync(mined.Workflow, ct);
            await drafts.PutAsync(family, mined with { Workflow = mined.Workflow with { Ref = published } }, ct);
            logger.LogInformation(
                "Mined workflow {WorkflowId}@{Version} for document family {Family} with {StageCount} stages; awaiting promotion",
                published.Id, published.Version, family, mined.Workflow.Stages.Count);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Mining never fails the run it follows.
            logger.LogError(e, "Mining failed for document family {Family}", family);
        }
    }

    private async Task QuietAsync(Task<RunSummary> shadow)
    {
        try
        {
            await shadow;
        }
        catch (Exception e) when (e is StageFailedException or OperationCanceledException)
        {
            // A stage failure is already published as WorkflowShadowFail.
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Workflow shadow run faulted");
        }
    }
}
