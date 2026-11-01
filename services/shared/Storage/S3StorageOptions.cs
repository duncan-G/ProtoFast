namespace ProtoFast.Storage;

public sealed class S3StorageOptions
{
    /// <summary>The bucket's name. Unset, it is ProtoStrap's BUCKET_&lt;NAME&gt; for the spec's bucket.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// LocalStack's endpoint under <c>aspire run</c> (AWS_ENDPOINT_URL_S3, which ProtoStrap.Hosting
    /// sets); unset in an account, so the SDK resolves the real endpoint from the region.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// The region presigned policies are signed for. Set alongside <see cref="ServiceUrl"/> in
    /// dev (LocalStack created the bucket in it); in production the client's own resolved region
    /// wins and this stays unset.
    /// </summary>
    public string? AwsRegion { get; set; }

    /// <summary>How long a presigned upload URL stays valid.</summary>
    public TimeSpan UploadUrlTtl { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan DownloadUrlTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>GOVERNANCE object-lock retention applied to frozen artifacts at write time.</summary>
    public int FrozenLockDays { get; set; } = 30;

    /// <summary>
    /// Off for a bucket without object lock. Freezing still writes the frozen artifact and its
    /// hash — only the storage-level retention is skipped, and the difference is logged rather
    /// than silently tolerated. ProtoStrap creates the documents bucket locked, LocalStack too.
    /// </summary>
    public bool ObjectLockEnabled { get; set; } = true;
}
