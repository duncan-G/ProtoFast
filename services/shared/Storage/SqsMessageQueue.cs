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

    private readonly SqsQueueOptions _options;
    private readonly ILogger<SqsMessageQueue> _logger;
    private readonly AmazonSQSClient _sqs;
    private readonly Lazy<Task<string>> _queueUrl;

    public SqsMessageQueue(SqsQueueOptions options, ILogger<SqsMessageQueue> logger)
    {
        _options = options;
        _logger = logger;
        _sqs = CreateClient(options);
        _queueUrl = new Lazy<Task<string>>(async () => (await _sqs.GetQueueUrlAsync(options.QueueName)).QueueUrl);
    }

    private bool IsFifo => _options.QueueName.EndsWith(".fifo", StringComparison.Ordinal);

    public async Task SendAsync<T>(T message, string groupId, CancellationToken ct = default)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = await _queueUrl.Value,
            MessageBody = JsonSerializer.Serialize(message, Json),
        };

        if (IsFifo)
        {
            request.MessageGroupId = groupId;

            // Each send is a distinct message; the SDK reuses this id when it retries the request.
            request.MessageDeduplicationId = Guid.NewGuid().ToString("N");
        }

        await _sqs.SendMessageAsync(request, ct);
    }

    public async Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(CancellationToken ct = default)
    {
        var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = await _queueUrl.Value,
            MaxNumberOfMessages = _options.MaxMessages,
            WaitTimeSeconds = (int)_options.WaitTime.TotalSeconds,
        }, ct);

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
                messages.Add(new QueueMessage<T>(body, message.ReceiptHandle));
            }
        }

        return messages;
    }

    public async Task DeleteAsync(string receiptHandle, CancellationToken ct = default) =>
        await _sqs.DeleteMessageAsync(await _queueUrl.Value, receiptHandle, ct);

    public void Dispose() => _sqs.Dispose();

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
