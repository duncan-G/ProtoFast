using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Pipeline;
using ProtoFast.Segmentation.Pipeline.Executors;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Worker;

/// <summary>
/// Picks up runs whose human gate has been decided.
///
/// <para><c>SubmitReviewDecision</c> writes the decision and re-queues the run — <c>api</c> never
/// touches a workflow, and a gated run holds no worker while it waits. This consumer notices the
/// decided review on the run row and resumes the workflow with it (plan §9.10).</para>
/// </summary>
public sealed class ReviewResumeConsumer(
    IAmazonSQS sqs,
    IWorkflowHost host,
    IServiceScopeFactory scopes,
    IOptions<QueueOptions> options,
    ILogger<ReviewResumeConsumer> logger) : BackgroundService
{
    private readonly QueueOptions _options = options.Value;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BatchPoll))
        {
            logger.LogWarning("No batch-poll queue configured; review resumption will not run.");
            return;
        }

        logger.LogInformation(
            "Resuming decided reviews from {QueueUrl} with up to {Concurrency} concurrent runs",
            _options.BatchPoll, _options.MaxConcurrentRuns);

        // A resumed review is a full run tail — structure, augmentation, publish — so it spends
        // nearly all of its wall time waiting on provider calls. Draining the queue one message at
        // a time made a worker that was almost entirely idle finish reviews at the rate of one
        // round trip each, so this lane gets the same bounded fan-out as the run lanes.
        using var slots = new SemaphoreSlim(_options.MaxConcurrentRuns);
        var inFlight = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await slots.WaitAsync(stoppingToken);

                var batch = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _options.BatchPoll,
                    // One resume per slot, for the same reason the run lanes take one: the work
                    // fans out inside the workflow, so holding several messages here would only
                    // delay the ones that are not being worked on.
                    MaxNumberOfMessages = 1,
                    WaitTimeSeconds = _options.WaitTimeSeconds,
                    VisibilityTimeout = _options.VisibilityTimeoutSeconds,
                }, stoppingToken);

                if (batch.Messages is not { Count: > 0 })
                {
                    slots.Release();
                    continue;
                }

                inFlight.Add(ProcessAndReleaseAsync(batch.Messages[0], slots, stoppingToken));
                inFlight.RemoveAll(t => t.IsCompleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                slots.Release();
                logger.LogError(ex, "Failed to receive from the review/batch queue; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Let the resumes in flight reach a checkpoint rather than dying inside a provider call.
        await Task.WhenAll(inFlight);
    }

    private async Task ProcessAndReleaseAsync(Message message, SemaphoreSlim slots, CancellationToken ct)
    {
        try
        {
            await ProcessAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown. The message stays invisible until its timeout expires and another worker
            // picks the decision up again.
        }
        catch (Exception ex)
        {
            // Nothing above this catches any more: the loop no longer awaits the work, so an
            // unhandled failure here would take the whole consumer down instead of one message.
            logger.LogError(ex, "Failed to resume a review; leaving the message for redelivery.");
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task ProcessAsync(Message message, CancellationToken ct)
    {
        var resume = JsonSerializer.Deserialize<ResumeMessage>(message.Body, Json);
        if (resume is null)
        {
            await sqs.DeleteMessageAsync(_options.BatchPoll, message.ReceiptHandle, CancellationToken.None);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var review = await db.ReviewTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReviewId == resume.ReviewId, ct);

        if (review is null or { Status: not "complete" } or { Decision: null })
        {
            // The decision has not landed yet. Leaving the message invisible and letting it come
            // back is cheaper than polling the database on a timer.
            logger.LogDebug("Review {ReviewId} is not decided yet; leaving it queued.", resume.ReviewId);
            return;
        }

        await using var heartbeat = new VisibilityHeartbeat(
            sqs, _options.BatchPoll, message.ReceiptHandle, _options.VisibilityTimeoutSeconds, logger, ct);

        var edits = JsonSerializer.Deserialize<List<Core.Model.ParagraphEdit>>(review.EditsJson, Json) ?? [];

        var outcome = await host.ResumeWithDecisionAsync(
            review.RunId,
            new ReviewDecision(review.ReviewId, review.Decision!.Value, review.Notes) { Edits = edits },
            ct);

        logger.LogInformation(
            "Review {ReviewId} resumed run {RunId} with outcome {Outcome}.",
            review.ReviewId, review.RunId, outcome);

        if (outcome != RunOutcome.Failed)
        {
            await sqs.DeleteMessageAsync(_options.BatchPoll, message.ReceiptHandle, CancellationToken.None);
        }
    }

    /// <summary>The message <c>api</c> sends after a decision is recorded.</summary>
    public sealed record ResumeMessage(string RunId, string ReviewId)
    {
        public string? TraceParent { get; init; }
    }
}
