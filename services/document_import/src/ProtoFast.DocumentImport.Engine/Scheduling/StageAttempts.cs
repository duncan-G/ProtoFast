using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Engine.Scheduling;

public sealed class StageAttempts(
    IExecutorResolver resolver,
    VerifierRunner verifiers,
    IRunLedger ledger,
    IOutcomeQueue outcomes,
    TimeProvider time,
    ILogger<StageAttempts> logger)
{
    public async Task<StageRecord> RunAsync(
        StageRequest request, ExecutorRef executorRef, bool isShadow, CancellationToken ct)
    {
        var executor = await resolver.ResolveAsync(executorRef, ct);
        var budget = request.Stage.Budget.MaxDuration;
        var started = time.GetTimestamp();

        StageResult result;
        IReadOnlyList<VerifierResult> verdicts;

        using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            if (budget > TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
            {
                attempt.CancelAfter(budget);
            }

            try
            {
                result = await executor.ExecuteAsync(request, attempt.Token);
                verdicts = await verifiers.RunAsync(request, result, executor.Tier, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The stage budget expired, not the run.
                result = Empty(time.GetElapsedTime(started));
                verdicts = [EngineChecks.OverTime(budget)];
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Executor {Executor} faulted on stage {StageId} of run {RunId}",
                    executorRef, request.Stage.Id, request.RunId);
                result = Empty(time.GetElapsedTime(started));
                verdicts = [EngineChecks.Faulted(e)];
            }
        }

        var record = new StageRecord(
            request.RunId, request.Stage, request.Inputs, executorRef, executor.Tier, result, verdicts, isShadow);
        await ledger.RecordAsync(record, ct);
        await outcomes.PublishAsync(Outcome.From(record, request.Signature.Family, time.GetUtcNow()), ct);
        return record;
    }

    private static StageResult Empty(TimeSpan elapsed) => new(ArtifactRef.None, null, new Cost(0, elapsed), []);
}
