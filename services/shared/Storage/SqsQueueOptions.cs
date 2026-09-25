namespace ProtoFast.Storage;

public sealed class SqsQueueOptions
{
    /// <summary>A name ending in <c>.fifo</c> is treated as a FIFO queue.</summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>LocalStack's endpoint in dev; unset in production, as for S3.</summary>
    public string? ServiceUrl { get; set; }

    public string? AwsRegion { get; set; }

    public int MaxMessages { get; set; } = 10;

    public TimeSpan WaitTime { get; set; } = TimeSpan.FromSeconds(20);
}
