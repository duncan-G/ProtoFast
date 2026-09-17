namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// A presigned upload slot (plan §18.3). It is recorded before the URL is handed out so
/// <c>SubmitRun</c> can check that the upload belongs to the caller without trusting a key from
/// the request — the browser could otherwise name any key in the bucket.
/// </summary>
public sealed class Upload
{
    public required string UploadId { get; set; }

    public required string OwnerSubject { get; set; }

    public required string FileName { get; set; }

    public long SizeBytes { get; set; }

    public bool WithLayout { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
