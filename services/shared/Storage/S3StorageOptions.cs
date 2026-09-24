namespace ProtoFast.Storage;

public sealed class S3StorageOptions
{
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// LocalStack's endpoint in dev; unset in production so the SDK resolves the real AWS
    /// endpoint from the region
    /// </summary>
    public string? ServiceUrl { get; set; }

    public required string AwsRegion { get; set; }

    /// <summary>How long a presigned upload URL stays valid.</summary>
    public TimeSpan UploadUrlTtl { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan DownloadUrlTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>GOVERNANCE object-lock retention applied to frozen artifacts at write time.</summary>
    public int FrozenLockDays { get; set; } = 30;

    /// <summary>
    /// Off for LocalStack, which does not implement object lock. Freezing still writes the frozen
    /// artifact and its hash — only the storage-level retention is skipped, and the difference is
    /// logged rather than silently tolerated.
    /// </summary>
    public bool ObjectLockEnabled { get; set; } = true;
}
