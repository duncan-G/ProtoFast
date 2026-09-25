using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Core;

namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// The entry point for a run. Classifies the input once, reads the bucket's mode once, and gives
/// control flow to exactly one owner: the discovery agent loop or the scheduler, never both.
///
/// <para>A discovery bucket with a promoted workflow is in shadow: on a ShadowSampleRate sample
/// the scheduler also runs the workflow as a separate run whose output is discarded, and its
/// terminal verdict is published as a bucket-level outcome. In scheduled mode every run publishes
/// one, which is how the bucket demotes. Every MineAfterRuns discovery runs the miner drafts a
/// workflow for a human to promote.</para>
/// </summary>
public sealed class RunDispatcher(
    IClassifier classifier,
    IBucketPolicyStore buckets,
    IRegistry registry,
    Scheduler scheduler,
    IDiscoveryAgent agent,
    AgentToolsFactory tools,
    IRunLedger ledger,
    IWorkflowMiner miner,
    IMinedWorkflowStore drafts,
    IOutcomeBus outcomes,
    IShadowSampler sampler,
    EngineOptions options,
    TimeProvider time,
    ILogger<RunDispatcher> logger)
{
    /// <param name="input">The run input, already in the artifact store under <see cref="ArtifactRef.InputStageId"/>.</param>
    public async Task<RunSummary> RunAsync(ArtifactRef input, CancellationToken ct)
    {
        var signature = await classifier.ClassifyAsync(input, ct);
        var bucket = await buckets.GetAsync(signature.Bucket, ct);
        var workflow = bucket.Workflow is { } promoted && await registry.IsPromotedAsync(promoted, ct)
            ? await registry.ResolveAsync(promoted, ct)
            : null;

        if (bucket.Mode == RunMode.Scheduled && workflow is not null)
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

        await MineIfDueAsync(signature.Bucket, ct);
        return summary;
    }

    private async Task<RunSummary> RunDiscoveryAsync(ArtifactRef input, Signature signature, CancellationToken ct)
    {
        var runId = DocumentImportIds.New();
        var trace = new TraceRef(runId);

        await ledger.OpenAsync(runId, signature, RunMode.Discovery, ct);
        await agent.RunAsync(input, tools.ForRun(runId, signature, input, trace, ct), trace, ct);

        // Only a run the agent finished is closed, so only finished runs are mined. An abandoned
        // run's attempts stay in the ledger and still count as outcomes.
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
                Outcome.ForWorkflow(signature.Bucket, workflow.Ref, passed: false, degraded: false, time.GetUtcNow()), ct);
            throw;
        }

        // A completed run means every stage passed; what is left to judge is whether the terminal
        // output passed clean or degraded.
        var terminal = workflow.TerminalStages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var degraded = summary.Stages
            .Where(s => terminal.Contains(s.StageId) && s is { IsShadow: false, Passed: true })
            .GroupBy(s => s.StageId)
            .Any(g => g.Last().Degraded);

        await outcomes.PublishAsync(
            Outcome.ForWorkflow(signature.Bucket, workflow.Ref, passed: true, degraded, time.GetUtcNow()), ct);
        return summary;
    }

    private async Task MineIfDueAsync(string bucket, CancellationToken ct)
    {
        var every = options.Thresholds.MineAfterRuns;
        if (every <= 0 || await ledger.CountAsync(bucket, RunMode.Discovery, ct) % every != 0)
        {
            return;
        }

        try
        {
            var runs = await ledger.RecentAsync(bucket, RunMode.Discovery, every, ct);
            if (await miner.MineAsync(bucket, runs, ct) is not { } mined)
            {
                return;
            }

            Dag.Validate(mined.Workflow);
            var published = await registry.PublishAsync(mined.Workflow, ct);
            await drafts.PutAsync(bucket, mined with { Workflow = mined.Workflow with { Ref = published } }, ct);
            logger.LogInformation(
                "Mined workflow {WorkflowId}@{Version} for bucket {Bucket} with {StageCount} stages; awaiting promotion",
                published.Id, published.Version, bucket, mined.Workflow.Stages.Count);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Mining is learning-plane work; it never fails the run it follows.
            logger.LogError(e, "Mining failed for bucket {Bucket}", bucket);
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
            // Already published as a WorkflowShadowFail, or the run was cancelled.
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Workflow shadow run faulted");
        }
    }
}
