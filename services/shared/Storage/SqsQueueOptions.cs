namespace ProtoFast.Storage;

public sealed class SqsQueueOptions
{
    /// <summary>
    /// The queue's URL. Unset, it is ProtoStrap's QUEUE_&lt;NAME&gt;_URL for the spec's queue, so
    /// the queue is never looked up by name.
    /// </summary>
    public string? QueueUrl { get; set; }

    /// <summary>
    /// The queue's name, looked up when there is no <see cref="QueueUrl"/>. A name (or URL)
    /// ending in <c>.fifo</c> is a FIFO queue.
    /// </summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>LocalStack's endpoint under <c>aspire run</c> (AWS_ENDPOINT_URL_SQS); unset in an account, as for S3.</summary>
    public string? ServiceUrl { get; set; }

    public string? AwsRegion { get; set; }

    public int MaxMessages { get; set; } = 10;

    /// <summary>
    /// How long a received message stays invisible before it is delivered again. Set on each
    /// receive rather than read from the queue, so the consumer knows exactly how long it has
    /// and can renew it (see <c>MessageLease</c>) while work on the message continues.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan WaitTime { get; set; } = TimeSpan.FromSeconds(20);
}
