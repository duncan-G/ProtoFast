using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Observability;
using ProtoFast.Segmentation.Pipeline;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Worker;

/// <summary>
/// The SQS consumer of plan §13.4.
///
/// <para>One message is one run. The visibility heartbeat extends the message while the run is
/// alive, so a long document is never redelivered under a healthy worker and a dead worker's run
/// returns to the queue within one timeout. That is the whole durability story: the worker holds
/// nothing, so replacing it mid-run costs at most one superstep.</para>
/// </summary>
public sealed class RunConsumer(
    IAmazonSQS sqs,
    IWorkflowHost host,
    IOptions<QueueOptions> options,
    ILogger<RunConsumer> logger,
    RunPriority lane) : BackgroundService
{
    /// <summary>Named so the worker's spans join the same trace as the <c>api</c> call that submitted the run.</summary>
    public static readonly ActivitySource ActivitySource = new("ProtoFast.Segmentation.Worker");

    private readonly QueueOptions _options = options.Value;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = lane == RunPriority.Bulk ? _options.Bulk : _options.Runs;

        if (string.IsNullOrWhiteSpace(queueUrl))
        {
            logger.LogWarning("No queue URL configured for the {Lane} lane; the consumer will not start.", lane);
            return;
        }

        logger.LogInformation(
            "Consuming the {Lane} lane ({QueueUrl}) with up to {Concurrency} concurrent runs",
            lane, queueUrl, _options.MaxConcurrentRuns);

        using var slots = new SemaphoreSlim(_options.MaxConcurrentRuns);
        var inFlight = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await slots.WaitAsync(stoppingToken);

                var batch = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    // One run per slot. Fan-out happens inside the workflow, so receiving several
                    // runs at once would only make each of them slower.
                    MaxNumberOfMessages = 1,
                    WaitTimeSeconds = _options.WaitTimeSeconds,
                    VisibilityTimeout = _options.VisibilityTimeoutSeconds,
                }, stoppingToken);

                if (batch.Messages is not { Count: > 0 })
                {
                    slots.Release();
                    continue;
                }

                var message = batch.Messages[0];
                inFlight.Add(ProcessAndReleaseAsync(message, queueUrl, slots, stoppingToken));
                inFlight.RemoveAll(t => t.IsCompleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A receive failure is a transport problem, not a run problem. Back off briefly
                // rather than spinning: an unreachable SQS would otherwise become a hot loop.
                slots.Release();
                logger.LogError(ex, "Failed to receive from the {Lane} lane; retrying.", lane);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Give in-flight runs a chance to checkpoint rather than being killed inside a provider
        // call. stop_grace_period in compose is set high enough for this (plan §23.4).
        await Task.WhenAll(inFlight);
    }

    private async Task ProcessAndReleaseAsync(
        Message message, string queueUrl, SemaphoreSlim slots, CancellationToken ct)
    {
        try
        {
            await ProcessAsync(message, queueUrl, ct);
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task ProcessAsync(Message message, string queueUrl, CancellationToken ct)
    {
        RunMessage? run;
        try
        {
            run = JsonSerializer.Deserialize<RunMessage>(message.Body, Json);
        }
        catch (JsonException ex)
        {
            // A message that will not parse can never succeed. Deleting it is better than letting
            // it cycle to the DLQ five times first, which would delay every real run behind it.
            logger.LogError(ex, "Discarding an unparseable message from {QueueUrl}: {Body}", queueUrl, message.Body);
            await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, CancellationToken.None);
            return;
        }

        if (run is null)
        {
            await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, CancellationToken.None);
            return;
        }

        // Restore the trace context the api half put on the message, so the submission and the
        // execution are one trace rather than two (plan §25.1).
        using var activity = ActivitySource.StartActivity(
            "segmentation.run", ActivityKind.Consumer, run.TraceParent ?? string.Empty);
        activity?.SetTag("run.id", run.RunId);

        await using var heartbeat = new VisibilityHeartbeat(
            sqs, queueUrl, message.ReceiptHandle, _options.VisibilityTimeoutSeconds, logger, ct);

        try
        {
            var outcome = await host.RunOrResumeAsync(
                run.RunId,
                run.FromPhase is { } phase ? (PipelinePhase)phase : null,
                ct);

            activity?.SetTag("run.outcome", outcome.ToString());

            if (outcome == RunOutcome.Failed)
            {
                // Leave the message for redelivery. maxReceiveCount on the redrive policy sends a
                // repeatedly-failing run to the DLQ rather than looping forever on provider spend.
                //
                // The span is coloured here as a backstop. WorkflowHost has usually already marked
                // it with the exception that caused this, and Fail leaves that richer record
                // alone — but an outcome that arrives with nothing recorded must still not leave
                // the trace green.
                activity.Fail("run.failed", "The run failed and will be redelivered.");
                logger.LogWarning("Run {RunId} failed; leaving the message for redelivery.", run.RunId);
                return;
            }

            if (outcome == RunOutcome.FailedPermanently)
            {
                // A document that cannot be read will not become readable on the fourth attempt.
                // The run row already carries the reason, so the message is deleted rather than
                // cycled to the DLQ — a DLQ entry is meant to be an alert, not a bad upload.
                activity.Fail("run.failed_permanently", "The run failed in a way redelivery cannot fix.");
                logger.LogWarning(
                    "Run {RunId} failed permanently; deleting the message rather than redelivering.", run.RunId);
                await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, CancellationToken.None);
                return;
            }

            await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, CancellationToken.None);
            logger.LogInformation("Run {RunId} finished with outcome {Outcome}.", run.RunId, outcome);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown. The message stays invisible until the timeout expires, then another worker
            // resumes the run from its checkpoint.
            logger.LogInformation("Worker stopping; run {RunId} will be resumed elsewhere.", run.RunId);
        }
        catch (Exception ex)
        {
            activity.Fail(ex);
            logger.LogError(ex, "Run {RunId} threw; leaving the message for redelivery.", run.RunId);
        }
    }
}
