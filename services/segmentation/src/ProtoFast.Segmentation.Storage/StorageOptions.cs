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

    /// <summary>
    /// The region the S3 and SQS clients sign for when <see cref="ServiceUrl"/> is set. LocalStack
    /// keeps queues per region exactly as AWS does, so a call signed for a region its init script
    /// did not create in gets a <c>QueueDoesNotExist</c> against a queue that is plainly there —
    /// which is why the AppHost passes the same region to both sides rather than letting each pick
    /// one up from the ambient environment. Ignored in production, where <c>ServiceUrl</c> is unset
    /// and the SDK resolves the region itself.
    /// </summary>
    public string Region { get; set; } = "us-west-2";

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

/// <summary>
/// Bound from <c>Seg_Conversion__*</c> (ingest plan §17.1). The worker's half of the conversion
/// contract: where the converter is, how long to wait for it, and what OCR policy to ask for.
/// </summary>
public sealed class ConversionOptions
{
    public const string SectionName = "Conversion";

    /// <summary>
    /// The converter's base address. Empty disables conversion entirely — every non-passthrough
    /// upload then fails phase 0 with a message saying so, rather than silently ingesting an
    /// unconverted PDF as if it were Markdown.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// The HTTP timeout, deliberately above the converter's own 8-minute budget so the converter's
    /// structured error wins the race and the phase fails with a reason rather than a cancellation.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    public bool OcrEnabled { get; set; } = true;

    public List<string> OcrLanguages { get; set; } = ["eng"];

    /// <summary>Pages above this are not OCR'd; a 300-page scan would hold a worker slot for a quarter of an hour.</summary>
    public int MaxOcrPages { get; set; } = 200;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);
}
