using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Core;
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
        using var activity = DocumentImportTelemetry.Source.StartActivity($"stage {request.Stage.Id}");
        activity?.SetTag("document_import.run_id", request.RunId);
        activity?.SetTag("document_import.stage_id", request.Stage.Id);
        activity?.SetTag("document_import.executor", executorRef.ToString());
        activity?.SetTag("document_import.shadow", isShadow);

        if (!isShadow)
        {
            await ledger.BeginStageAsync(request.RunId, request.Stage.Id, ct);
        }

        var executor = await resolver.ResolveAsync(executorRef, ct);
        activity?.SetTag("document_import.tier", executor.Tier.ToString());
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
                activity?.AddException(e);
                logger.LogWarning(e, "Executor {Executor} faulted on stage {StageId} of run {RunId}",
                    executorRef, request.Stage.Id, request.RunId);
                result = Empty(time.GetElapsedTime(started));
                verdicts = [EngineChecks.Faulted(e)];
            }
        }

        var record = new StageRecord(
            request.RunId, request.Stage, request.Inputs, executorRef, executor.Tier, result, verdicts, isShadow);
        activity?.SetTag("document_import.passed", record.Passed);
        activity?.SetTag("document_import.degraded", record.Degraded);
        if (!record.Passed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, string.Join(" ", verdicts
                .Where(v => v.Verdict == Verdict.Fail)
                .Select(v => $"{v.VerifierId}: {v.Reason}")));
        }

        await ledger.RecordAsync(record, ct);
        await outcomes.PublishAsync(Outcome.From(record, request.DocumentSignature.Family, time.GetUtcNow()), ct);
        return record;
    }

    private static StageResult Empty(TimeSpan elapsed) => new(ArtifactRef.None, null, new Cost(0, elapsed), []);
}
