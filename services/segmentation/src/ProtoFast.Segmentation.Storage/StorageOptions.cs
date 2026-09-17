namespace ProtoFast.Segmentation.Storage;

/// <summary>Bound from <c>Seg_Storage__*</c> / <c>Api_Segmentation__*</c> (plan §20).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// LocalStack's endpoint in dev; unset in production so the SDK resolves the real AWS
    /// endpoint from the region (plan §20.1).
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>How long a presigned upload URL stays valid (plan §18.3).</summary>
    public TimeSpan UploadUrlTtl { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan DownloadUrlTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>GOVERNANCE object-lock retention applied to frozen artifacts at write time (plan §9.11).</summary>
    public int FrozenLockDays { get; set; } = 365;

    /// <summary>
    /// Off for LocalStack, which does not implement object lock. Freezing still writes the frozen
    /// artifact and its hash — only the storage-level retention is skipped, and the difference is
    /// logged rather than silently tolerated.
    /// </summary>
    public bool ObjectLockEnabled { get; set; } = true;
}

/// <summary>Bound from <c>Seg_Queues__*</c> (plan §20.2).</summary>
public sealed class QueueOptions
{
    public const string SectionName = "Queues";

    public string Runs { get; set; } = string.Empty;

    public string Bulk { get; set; } = string.Empty;

    public string BatchPoll { get; set; } = string.Empty;

    /// <summary>Concurrent runs per worker process. Start at 8 on a t4g.medium (plan §29.3).</summary>
    public int MaxConcurrentRuns { get; set; } = 8;

    /// <summary>Must match the queue's own visibility timeout; the heartbeat renews inside it.</summary>
    public int VisibilityTimeoutSeconds { get; set; } = 900;

    /// <summary>Long-poll wait. 20s is the SQS maximum and cuts empty receives to near zero.</summary>
    public int WaitTimeSeconds { get; set; } = 20;
}
