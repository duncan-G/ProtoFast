using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A document a ThePlot user has brought onto their desk: an upload whose bytes have landed in
/// object storage. Lives in the <c>plot</c> schema of the <c>protofast</c> database
/// (<see cref="ThePlotDbContext"/>).
///
/// <para>Keyed by the upload id, so the <see cref="DocumentUpload"/> row that minted the presigned
/// URL, the S3 object and this row all share one identifier. A document exists only once the API
/// has confirmed the object is in the bucket; nothing further is done to it yet — no chapter
/// extraction, no adaptation — so the row records the source exactly as it was received.</para>
/// </summary>
public sealed class Document : IDateStamped
{
    /// <summary>The upload id: a 26-character lowercase Crockford ULID from <c>DocumentImportIds.New()</c>.</summary>
    public required string Id { get; set; }

    /// <summary>
    /// The owner's subject from the internal JWT. Stamped from the caller on insert and never
    /// taken from a request; reads are filtered to it and writes for another owner are refused.
    /// </summary>
    public string UserId { get; set; } = "";

    /// <summary>The title shown on the desk. Derived from the file name at import; editable later.</summary>
    public required string Name { get; set; }

    /// <summary>The file name the client supplied, kept for display.</summary>
    public required string FileName { get; set; }

    public required long SizeBytes { get; set; }

    /// <summary>The canonical media type of the resolved source format.</summary>
    public required string MediaType { get; set; }

    /// <summary>The resolved source format's extension, with the leading dot (<c>.docx</c>).</summary>
    public required string FileExtension { get; set; }

    /// <summary>The object key the bytes live at in the document bucket.</summary>
    public required string StorageKey { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
