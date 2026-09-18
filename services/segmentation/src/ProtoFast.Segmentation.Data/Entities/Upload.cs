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

    /// <summary>
    /// Whether this format produces layout metadata — <em>not</em> whether the user also uploaded
    /// a layout file. The old reading is the wrong one: since the ingest plan the converter is
    /// what emits <c>.layout.json</c>, and the format decides whether there is any geometry to
    /// emit at all (ingest plan §13.1).
    /// </summary>
    public bool WithLayout { get; set; }

    /// <summary>The validated media type the POST policy was signed for.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// The canonical extension from <c>SourceFormats</c>. With <see cref="OwnerSubject"/> and
    /// <see cref="UploadId"/> this reconstructs the source key — which is why it is stored rather
    /// than re-derived from <see cref="FileName"/>, a string the caller chose.
    /// </summary>
    public string SourceExtension { get; set; } = string.Empty;

    /// <summary>False for <c>.md</c>, <c>.markdown</c> and <c>.txt</c>: the source is the output.</summary>
    public bool RequiresConversion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
