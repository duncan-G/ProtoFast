namespace ProtoFast.Storage;

public sealed class SqsQueueOptions
{
    /// <summary>A name ending in <c>.fifo</c> is treated as a FIFO queue.</summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>LocalStack's endpoint in dev; unset in production, as for S3.</summary>
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
