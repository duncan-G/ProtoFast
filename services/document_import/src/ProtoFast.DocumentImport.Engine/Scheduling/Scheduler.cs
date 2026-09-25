using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Core;

namespace ProtoFast.DocumentImport.Engine;

public interface IScheduler
{
    Task<RunSummary> RunAsync(WorkflowDefinition workflow, ArtifactRef input, CancellationToken ct);
}

public interface IShadowSampler
{
    bool Take(double rate);
}

public sealed class RandomShadowSampler : IShadowSampler
{
    public bool Take(double rate) => rate > 0 && Random.Shared.NextDouble() < rate;
}

/// <summary>
/// Pure control flow. No reasoning, no artifact inspection. A stage starts as soon as every
/// dependency has an output; independent stages run concurrently. A stage failure cancels the rest
/// of the run.
/// </summary>
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

    /// <summary>Runs an already-classified input; the dispatcher classifies once to choose the run mode.</summary>
    public async Task<RunSummary> RunAsync(
        WorkflowDefinition workflow, ArtifactRef input, Signature signature, CancellationToken ct)
    {
        Dag.Validate(workflow);

        var runId = DocumentImportIds.New();
        var policy = await FreezePolicyAsync(workflow, signature.Bucket, ct);   // frozen for this run
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
                // Bounded by stage budgets; never touches outputs. Awaited on failure too, so no
                // shadow outlives the token source it runs under.
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
        var tier = row.Tier;
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

            tier = row.Ladder.Below(tier);              // one step left, this run only
        }
    }

    private async Task<IReadOnlyDictionary<string, PolicyRow>> FreezePolicyAsync(
        WorkflowDefinition workflow, string bucket, CancellationToken ct)
    {
        var snapshot = await store.SnapshotAsync(bucket, workflow.Stages.Select(s => s.Id), ct);
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
            // A shadow never affects the run; it has already been recorded if it got that far.
            logger.LogWarning(e, "Shadow attempt failed outside its executor");
        }
    }
}

public sealed class StageFailedException(StageRequest request, IReadOnlyList<VerifierResult> verdicts)
    : Exception(
        $"Stage '{request.Stage.Id}' of run {request.RunId} failed at Orchestrator: "
        + string.Join("; ", verdicts.Where(v => v.Verdict == Verdict.Fail).Select(v => $"{v.VerifierId}: {v.Reason}")))
{
    public StageRequest Request { get; } = request;
    public IReadOnlyList<VerifierResult> Verdicts { get; } = verdicts;
}
