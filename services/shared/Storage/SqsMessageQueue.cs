using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

internal sealed class SqsMessageQueue : IMessageQueue, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // W3C trace context, carried as message attributes so a consumer can continue the sender's trace.
    private const string TraceParentAttribute = "traceparent";
    private const string TraceStateAttribute = "tracestate";

    private const int MaxReceiveBatch = 10;

    private readonly SqsQueueOptions _options;
    private readonly ILogger<SqsMessageQueue> _logger;
    private readonly AmazonSQSClient _sqs;
    private string? _queueUrl;
    private int? _maxReceiveCount;
    private bool _maxReceiveCountRead;

    public SqsMessageQueue(SqsQueueOptions options, ILogger<SqsMessageQueue> logger)
    {
        _options = options;
        _logger = logger;
        _sqs = CreateClient(options);
    }

    public string Name => _options.QueueName;

    private bool IsFifo => _options.QueueName.EndsWith(".fifo", StringComparison.Ordinal);

    public async Task SendAsync<T>(T message, string groupId, CancellationToken ct = default)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = await QueueUrlAsync(ct),
            MessageBody = JsonSerializer.Serialize(message, Json),
            MessageAttributes = TraceAttributes(Activity.Current),
        };

        if (IsFifo)
        {
            request.MessageGroupId = groupId;

            // Each send is a distinct message; the SDK reuses this id when it retries the request.
            request.MessageDeduplicationId = Guid.NewGuid().ToString("N");
        }

        await _sqs.SendMessageAsync(request, ct);
    }

    public Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(CancellationToken ct = default) =>
        ReceiveAsync<T>(_options.MaxMessages, ct);

    public async Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(int maxMessages, CancellationToken ct = default)
    {
        var queueUrl = await QueueUrlAsync(ct);

        // Taken before the request, as SQS starts each message's visibility timeout during it.
        var receivedAt = DateTimeOffset.UtcNow;
        var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = Math.Clamp(maxMessages, 1, MaxReceiveBatch),
            WaitTimeSeconds = (int)_options.WaitTime.TotalSeconds,
            VisibilityTimeout = (int)_options.VisibilityTimeout.TotalSeconds,
            MessageAttributeNames = [TraceParentAttribute, TraceStateAttribute],
            MessageSystemAttributeNames = [MessageSystemAttributeName.ApproximateReceiveCount],
        }, ct);

        var maxReceiveCount = response.Messages is { Count: > 0 } ? await MaxReceiveCountAsync(ct) : null;

        var messages = new List<QueueMessage<T>>();
        foreach (var message in response.Messages ?? [])
        {
            T? body;
            try
            {
                body = JsonSerializer.Deserialize<T>(message.Body, Json);
            }
            catch (JsonException e)
            {
                // Left undeleted, so it reaches the dead-letter queue after the receive limit.
                _logger.LogWarning(e, "Unreadable message {MessageId} on {Queue}", message.MessageId, _options.QueueName);
                continue;
            }

            if (body is not null)
            {
                messages.Add(new QueueMessage<T>(
                    body,
                    message.ReceiptHandle,
                    _options.VisibilityTimeout,
                    receivedAt,
                    TraceParent(message),
                    IsLastDelivery(message, maxReceiveCount)));
            }
        }

        return messages;
    }

    public async Task DeleteAsync(string receiptHandle, CancellationToken ct = default) =>
        await _sqs.DeleteMessageAsync(await QueueUrlAsync(ct), receiptHandle, ct);

    public async Task ExtendVisibilityAsync(string receiptHandle, TimeSpan timeout, CancellationToken ct = default) =>
        await _sqs.ChangeMessageVisibilityAsync(
            await QueueUrlAsync(ct), receiptHandle, (int)Math.Ceiling(timeout.TotalSeconds), ct);

    public void Dispose() => _sqs.Dispose();

    // Only a successful lookup is cached, so a queue that did not exist yet is looked up again.
    private async Task<string> QueueUrlAsync(CancellationToken ct) =>
        _queueUrl ??= (await _sqs.GetQueueUrlAsync(_options.QueueName, ct)).QueueUrl;

    /// <summary>The redrive policy's receive limit, or null for a queue with no dead-letter queue.</summary>
    private async Task<int?> MaxReceiveCountAsync(CancellationToken ct)
    {
        // Only a successful lookup is cached, as for the queue URL.
        if (_maxReceiveCountRead)
        {
            return _maxReceiveCount;
        }

        var attributes = await _sqs.GetQueueAttributesAsync(
            await QueueUrlAsync(ct), [QueueAttributeName.RedrivePolicy], ct);
        _maxReceiveCount = attributes.Attributes?.GetValueOrDefault(QueueAttributeName.RedrivePolicy) is { Length: > 0 } policy
            ? ParseMaxReceiveCount(policy)
            : null;
        _maxReceiveCountRead = true;
        return _maxReceiveCount;
    }

    // SQS returns the count as a number; LocalStack echoes the string it was given.
    private static int? ParseMaxReceiveCount(string redrivePolicy)
    {
        using var json = JsonDocument.Parse(redrivePolicy);
        if (!json.RootElement.TryGetProperty("maxReceiveCount", out var count))
        {
            return null;
        }

        var text = count.ValueKind == JsonValueKind.String ? count.GetString() : count.GetRawText();
        return int.TryParse(text, out var max) ? max : null;
    }

    private static bool IsLastDelivery(Message message, int? maxReceiveCount) =>
        maxReceiveCount is { } max
        && message.Attributes?.GetValueOrDefault(MessageSystemAttributeName.ApproximateReceiveCount) is { } received
        && int.TryParse(received, out var count)
        && count >= max;

    private static Dictionary<string, MessageAttributeValue>? TraceAttributes(Activity? activity)
    {
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C)
        {
            return null;
        }

        var attributes = new Dictionary<string, MessageAttributeValue>
        {
            [TraceParentAttribute] = new() { DataType = "String", StringValue = activity.Id },
        };
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            attributes[TraceStateAttribute] = new() { DataType = "String", StringValue = activity.TraceStateString };
        }

        return attributes;
    }

    private static ActivityContext TraceParent(Message message)
    {
        var attributes = message.MessageAttributes;
        if (attributes is null || !attributes.TryGetValue(TraceParentAttribute, out var traceParent))
        {
            return default;
        }

        var traceState = attributes.GetValueOrDefault(TraceStateAttribute)?.StringValue;
        return ActivityContext.TryParse(traceParent.StringValue, traceState, isRemote: true, out var context)
            ? context
            : default;
    }

    private static AmazonSQSClient CreateClient(SqsQueueOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            return new AmazonSQSClient();
        }

        return new AmazonSQSClient(
            new BasicAWSCredentials("localstack", "localstack"),
            new AmazonSQSConfig { ServiceURL = options.ServiceUrl, AuthenticationRegion = options.AwsRegion });
    }
}
