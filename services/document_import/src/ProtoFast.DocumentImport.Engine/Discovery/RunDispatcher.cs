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
    IClassifier classifier,
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
        var signature = await classifier.ClassifyAsync(input, ct);
        var family = await families.GetAsync(signature.Family, ct);
        var workflow = family.Workflow is { } promoted && await registry.IsPromotedAsync(promoted, ct)
            ? await registry.ResolveAsync(promoted, ct)
            : null;

        if (family.Mode == RunMode.Scheduled && workflow is not null)
        {
            return await RunScheduledAsync(workflow, input, signature, ct);
        }

        var shadow = workflow is not null && sampler.Take(options.Thresholds.ShadowSampleRate)
            ? QuietAsync(RunScheduledAsync(workflow, input, signature, ct))
            : Task.CompletedTask;

        RunSummary summary;
        try
        {
            summary = await RunDiscoveryAsync(input, signature, ct);
        }
        finally
        {
            await shadow;
        }

        await MineIfDueAsync(signature.Family, ct);
        return summary;
    }

    private async Task<RunSummary> RunDiscoveryAsync(ArtifactRef input, Signature signature, CancellationToken ct)
    {
        var runId = DocumentImportIds.New();
        var trace = new TraceRef(runId);

        await ledger.OpenAsync(runId, signature, RunMode.Discovery, ct);
        await agent.RunAsync(input, tools.ForRun(runId, signature, input, trace, ct), trace, ct);

        // Only finished runs are closed, so only finished runs are mined.
        await ledger.CloseAsync(runId, trace, ct);
        return await ledger.SummariseAsync(runId, ct);
    }

    private async Task<RunSummary> RunScheduledAsync(
        WorkflowDefinition workflow, ArtifactRef input, Signature signature, CancellationToken ct)
    {
        RunSummary summary;
        try
        {
            summary = await scheduler.RunAsync(workflow, input, signature, ct);
        }
        catch (StageFailedException)
        {
            await outcomes.PublishAsync(
                Outcome.ForWorkflow(signature.Family, workflow.Ref, passed: false, degraded: false, time.GetUtcNow()), ct);
            throw;
        }

        // Reaching here means every stage passed.
        var terminal = workflow.TerminalStages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var degraded = summary.Stages
            .Where(s => terminal.Contains(s.StageId) && s is { IsShadow: false, Passed: true })
            .GroupBy(s => s.StageId)
            .Any(g => g.Last().Degraded);

        await outcomes.PublishAsync(
            Outcome.ForWorkflow(signature.Family, workflow.Ref, passed: true, degraded, time.GetUtcNow()), ct);
        return summary;
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
