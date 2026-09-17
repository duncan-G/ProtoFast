using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Pipeline.Executors;

namespace ProtoFast.Segmentation.Pipeline;

/// <summary>
/// Drives MAF's in-process runner over the segmentation workflow, with checkpoints in S3.
///
/// <para>The checkpoint store is what makes a mid-run deploy safe (plan §13.2): the worker holds
/// no run state, so when its container stops, the SQS message's visibility expires and another
/// worker resumes the same run from its last superstep. The session id is the run id, which is
/// what lets "resume this run" be a lookup rather than a search.</para>
/// </summary>
public sealed class WorkflowHost(
    SegmentationWorkflowFactory factory,
    ICheckpointStore<JsonElement> checkpoints,
    RunJournal journal,
    IServiceScopeFactory scopes,
    ILogger<WorkflowHost> logger) : IWorkflowHost
{
    private readonly CheckpointManager _checkpointManager = CheckpointManager.CreateJson(checkpoints);

    public async Task<RunOutcome> RunOrResumeAsync(
        string runId, PipelinePhase? fromPhase = null, CancellationToken ct = default)
    {
        var state = await ReadRunStateAsync(runId, ct);

        if (state.Cancelled)
        {
            logger.LogInformation("Run {RunId} was cancelled; not starting.", runId);
            return RunOutcome.Cancelled;
        }

        // A run that already published is done, and its message is simply a redelivery — the
        // classic SQS at-least-once case, which happens whenever a worker succeeds and then dies
        // before deleting the message.
        //
        // This has to be checked BEFORE resuming: resuming the final checkpoint of a finished
        // workflow produces a run with no work left to do and no output event to end on, so the
        // event loop below would block forever and hold the worker slot with it.
        if (state.Published && fromPhase is null)
        {
            logger.LogInformation("Run {RunId} has already published; treating the message as a redelivery.", runId);
            return RunOutcome.Completed;
        }

        var workflow = factory.Build();

        // A run with a checkpoint is resumed from it; one without is started. RerunFrom is the
        // exception: it deliberately re-executes phases, so it starts fresh and lets each phase's
        // idempotency gate decide what to reuse.
        var latest = fromPhase is null ? await LatestCheckpointAsync(runId, ct) : null;

        try
        {
            await using var run = latest is null
                ? await InProcessExecution.RunStreamingAsync(workflow, new RunStart(runId) { FromPhase = fromPhase }, _checkpointManager, runId, ct)
                : await InProcessExecution.ResumeStreamingAsync(workflow, latest, _checkpointManager, ct);

            return await PumpAsync(runId, run, ct);
        }
        catch (PipelineFailureException ex)
        {
            await journal.FailAsync(runId, ex.Phase, ex.Message, CancellationToken.None);
            logger.LogError(ex, "Run {RunId} failed in phase {Phase}", runId, ex.Phase);
            return RunOutcome.Failed;
        }
    }

    public async Task<RunOutcome> ResumeWithDecisionAsync(
        string runId, ReviewDecision decision, CancellationToken ct = default)
    {
        var latest = await LatestCheckpointAsync(runId, ct);
        if (latest is null)
        {
            // No checkpoint means the gate was never reached, or the checkpoints have expired.
            // Re-running from the structure phase reproduces the tree and re-applies the gate,
            // which is the only honest way to act on a decision whose context is gone.
            logger.LogWarning(
                "Run {RunId} has no checkpoint to resume with a review decision; re-running from phase 5.", runId);
            return await RunOrResumeAsync(runId, PipelinePhase.InferStructure, ct);
        }

        var workflow = factory.Build();

        await using var run = await InProcessExecution.ResumeStreamingAsync(workflow, latest, _checkpointManager, ct);

        // The decision is delivered from inside the same event loop that observes the request.
        // Watching the stream once to find the request and then again to drain it would be two
        // consumers of one channel: the second iteration blocks forever on events the first
        // already took.
        return await PumpAsync(runId, run, ct, decision);
    }

    /// <summary>
    /// Drains the workflow's event stream, translating MAF's events into run outcomes.
    ///
    /// <para>A <see cref="RequestInfoEvent"/> means the run has suspended on the human gate. That
    /// is a normal, successful outcome for this message: the run is checkpointed and the review
    /// task row is written, so the message is deleted and <c>SubmitReviewDecision</c> is what
    /// wakes the run up again. Holding the message instead would burn a worker slot for however
    /// long a person takes.</para>
    /// </summary>
    private async Task<RunOutcome> PumpAsync(
        string runId, StreamingRun run, CancellationToken ct, ReviewDecision? decision = null)
    {
        await foreach (var evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case RequestInfoEvent { Data: ExternalRequest request } when decision is not null:
                    // The response has to be addressed to the port's own request id, which is only
                    // available here — so the decision is delivered in place and the loop carries
                    // on with the run it just unblocked.
                    logger.LogInformation(
                        "Run {RunId}: delivering the {Decision} decision to the gate.",
                        runId, decision.Kind);
                    await run.SendResponseAsync(request.CreateResponse(decision));
                    decision = null;
                    break;

                case RequestInfoEvent:
                    logger.LogInformation("Run {RunId} suspended for human review.", runId);
                    return RunOutcome.AwaitingReview;

                case WorkflowOutputEvent output when output.Data is PublishComplete published:
                    logger.LogInformation(
                        "Run {RunId} completed: {Paragraphs} paragraphs, tree {TreeHash}",
                        runId, published.ParagraphCount, published.TreeHash);
                    return RunOutcome.Completed;

                case ExecutorFailedEvent failure:
                    // MAF catches whatever an executor throws and reports it as an event rather
                    // than propagating it, so this — not a catch block around the run — is where
                    // a phase failure has to be recorded. Without it the run would stop with a
                    // null error and ThePlot would show a document that simply stalled.
                    await RecordFailureAsync(runId, failure.ExecutorId, Describe(failure.Data));
                    logger.LogError(
                        "Run {RunId}: executor {Executor} failed: {Message}",
                        runId, failure.ExecutorId, Describe(failure.Data));
                    return RunOutcome.Failed;

                case WorkflowErrorEvent error:
                    await RecordFailureAsync(runId, executorId: null, Describe(error.Data));
                    logger.LogError("Run {RunId}: workflow error: {Error}", runId, Describe(error.Data));
                    return RunOutcome.Failed;

                case SuperStepCompletedEvent when await IsCancelledAsync(runId, ct):
                    // Cancellation is observed at a superstep boundary (plan §17), so the run stops
                    // between phases rather than mid-provider-call.
                    logger.LogInformation("Run {RunId} was cancelled mid-flight.", runId);
                    await run.CancelRunAsync();
                    return RunOutcome.Cancelled;
            }
        }

        return RunOutcome.Completed;
    }

    /// <summary>
    /// Writes the failure onto the run and its phase. Uses <see cref="CancellationToken.None"/>:
    /// a run that failed because the worker is shutting down still has to record why, and the
    /// token that stopped it would cancel the write too.
    /// </summary>
    private async Task RecordFailureAsync(string runId, string? executorId, string message)
    {
        var phase = executorId is null ? null : ExecutorIds.PhaseFor(executorId);

        await journal.FailAsync(
            runId,
            // An unrecognised executor is still a real failure; attributing it to Ingest would be
            // a lie, so it lands on Publish — the phase that means "the run did not finish".
            phase ?? PipelinePhase.Publish,
            message,
            CancellationToken.None);
    }

    /// <summary>
    /// The message a person should see. An exception's own message is the useful part — the phase
    /// failures this pipeline raises are written for a reader — so the stack trace is left to the
    /// log rather than stored on the run.
    /// </summary>
    private static string Describe(object? data) => data switch
    {
        PipelineFailureException failure => failure.Message,
        Exception { InnerException: PipelineFailureException inner } => inner.Message,
        Exception exception => $"{exception.GetType().Name}: {exception.Message}",
        null => "The run stopped without reporting a reason.",
        _ => data.ToString() ?? "The run stopped without reporting a reason.",
    };

    private async Task<CheckpointInfo?> LatestCheckpointAsync(string runId, CancellationToken ct)
    {
        var index = await checkpoints.RetrieveIndexAsync(runId);
        return index.LastOrDefault();
    }

    private sealed record RunState(bool Cancelled, bool Published);

    private async Task<RunState> ReadRunStateAsync(string runId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        return await db.Runs
            .AsNoTracking()
            .Where(r => r.RunId == runId)
            .Select(r => new RunState(r.Cancelled, r.PublishedAt != null))
            .FirstOrDefaultAsync(ct)
            ?? new RunState(Cancelled: false, Published: false);
    }

    private async Task<bool> IsCancelledAsync(string runId, CancellationToken ct) =>
        (await ReadRunStateAsync(runId, ct)).Cancelled;
}
