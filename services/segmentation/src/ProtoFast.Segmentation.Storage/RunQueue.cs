using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Storage;

/// <summary>
/// Sends work onto the three queues of plan §16. <c>api</c> holds only this half of the queue —
/// it submits and never receives.
/// </summary>
public interface IRunQueue
{
    Task SendRunAsync(RunMessage message, CancellationToken ct = default);

    /// <summary>
    /// Schedules a provider-batch poll for later. The delay is SQS's, not a timer's: a pending
    /// batch then costs no worker time at all — the message simply becomes visible when it is
    /// worth checking again (plan §14.8).
    /// </summary>
    Task SendBatchPollAsync(BatchPollMessage message, TimeSpan delay, CancellationToken ct = default);

    string QueueUrlFor(RunPriority priority);
}

public sealed class SqsRunQueue(IAmazonSQS sqs, IOptions<QueueOptions> options) : IRunQueue
{
    /// <summary>SQS caps <c>DelaySeconds</c> at 15 minutes; longer waits re-delay on each poll.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    private readonly QueueOptions _options = options.Value;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string QueueUrlFor(RunPriority priority) =>
        priority == RunPriority.Bulk ? _options.Bulk : _options.Runs;

    public Task SendRunAsync(RunMessage message, CancellationToken ct = default) =>
        sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = QueueUrlFor(message.Priority),
            MessageBody = JsonSerializer.Serialize(WithCurrentTrace(message), Json),
        }, ct);

    public Task SendBatchPollAsync(BatchPollMessage message, TimeSpan delay, CancellationToken ct = default) =>
        sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _options.BatchPoll,
            MessageBody = JsonSerializer.Serialize(
                message with { TraceParent = Activity.Current?.Id ?? message.TraceParent }, Json),
            DelaySeconds = (int)Math.Clamp(delay.TotalSeconds, 0, MaxDelay.TotalSeconds),
        }, ct);

    /// <summary>
    /// Stamps the current trace context onto the message. Without it the <c>api</c> span that
    /// submitted the run and the worker spans that executed it are two unrelated traces, and the
    /// question "why was this document slow?" has no single answer to look at (plan §25.1).
    /// </summary>
    private static RunMessage WithCurrentTrace(RunMessage message) =>
        Activity.Current is { } activity
            ? message with { TraceParent = activity.Id, TraceState = activity.TraceStateString }
            : message;
}
