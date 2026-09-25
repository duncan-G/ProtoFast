using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One document upload as the API accepted it when it minted the presigned URL. Keyed by the
/// import id that <c>DocumentImportIds.New()</c> mints, which the S3 key, the queue message and
/// the import run's artifacts all carry, so this row is the anchor for the upload end to end.
/// </summary>
public sealed class DocumentUpload : IDateStamped
{
    /// <summary>A 26-character lowercase Crockford ULID; sorts by creation time.</summary>
    public required string UploadId { get; set; }

    /// <summary>
    /// The owner's subject from the internal JWT. Stamped from the caller on insert and never
    /// taken from a request; reads are filtered to it and writes for another owner are refused.
    /// </summary>
    public string UserId { get; set; } = "";

    /// <summary>The file name the client supplied, kept for display; the S3 key uses the id.</summary>
    public required string FileName { get; set; }

    public required long SizeBytes { get; set; }

    /// <summary>The canonical media type of the resolved source format, not the client's header.</summary>
    public required string MediaType { get; set; }

    /// <summary>The resolved source format's extension, with the leading dot (<c>.pdf</c>).</summary>
    public required string FileExtension { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
