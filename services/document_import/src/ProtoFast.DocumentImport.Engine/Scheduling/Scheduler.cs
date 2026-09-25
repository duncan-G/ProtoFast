using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Scheduling;

public sealed class Scheduler(
    IClassifier classifier,
    IPolicyStore store,
    PolicyGate gate,
    StageAttempts attempts,
    IRunLedger ledger,
    IShadowSampler sampler,
    EngineOptions options,
    ILogger<Scheduler> logger) : IScheduler
{
    public async Task<RunSummary> RunAsync(WorkflowDefinition workflow, ArtifactRef input, CancellationToken ct)
    {
        var signature = await classifier.ClassifyAsync(input, ct);
        return await RunAsync(workflow, input, signature, ct);
    }

    public async Task<RunSummary> RunAsync(
        WorkflowDefinition workflow, ArtifactRef input, Signature signature, CancellationToken ct)
    {
        StageGraph.Validate(workflow);

        var runId = DocumentImportIds.New();
        var policy = await FreezePolicyAsync(workflow, signature.Family, ct);
        var outputs = new ConcurrentDictionary<string, ArtifactRef>(StringComparer.Ordinal);
        var shadows = new ConcurrentBag<Task>();

        await ledger.OpenAsync(runId, signature, RunMode.Scheduled, ct);
        try
        {
            using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                await workflow.Stages.RunDagAsync(onError: run.Cancel, body: async stage =>
                {
                    var row = policy[stage.Id];
                    IReadOnlyList<ArtifactRef> inputs = stage.DependsOn.Count == 0
                        ? [input]
                        : stage.DependsOn.Select(id => outputs[id]).ToList();
                    var request = new StageRequest(runId, stage, signature, inputs);

                    var record = await ExecuteWithEscalationAsync(request, row, run.Token);
                    outputs[stage.Id] = record.Output;

                    if (row.Shadow is { } shadow && sampler.Take(options.Thresholds.ShadowSampleRate))
                    {
                        shadows.Add(QuietAsync(attempts.RunAsync(request, row.Ladder[shadow], isShadow: true, run.Token)));
                    }
                });
            }
            finally
            {
                // Awaited on failure too, so no shadow outlives `run`.
                await Task.WhenAll(shadows);
            }
        }
        finally
        {
            await ledger.CloseAsync(runId, null, CancellationToken.None);
        }

        return await ledger.SummariseAsync(runId, ct);
    }

    private async Task<StageRecord> ExecuteWithEscalationAsync(StageRequest request, PolicyRow row, CancellationToken ct)
    {
        var tier = row.Primary;
        while (true)
        {
            var record = await attempts.RunAsync(request, row.Ladder[tier], isShadow: false, ct);
            if (record.Passed)
            {
                return record;
            }

            if (tier == Tier.Orchestrator)
            {
                throw new StageFailedException(request, record.Verdicts);
            }

            tier = row.Ladder.Below(tier);
        }
    }

    private async Task<IReadOnlyDictionary<string, PolicyRow>> FreezePolicyAsync(
        WorkflowDefinition workflow, string family, CancellationToken ct)
    {
        var snapshot = await store.SnapshotAsync(family, workflow.Stages.Select(s => s.Id), ct);
        var frozen = new Dictionary<string, PolicyRow>(StringComparer.Ordinal);
        foreach (var stage in workflow.Stages)
        {
            frozen[stage.Id] = await gate.ClampAsync(snapshot[stage.Id], stage, ct);
        }

        return frozen;
    }

    private async Task QuietAsync(Task<StageRecord> shadow)
    {
        try
        {
            await shadow;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Shadow attempt failed outside its executor");
        }
    }
}
