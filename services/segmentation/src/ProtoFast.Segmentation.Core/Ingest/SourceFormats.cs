namespace ProtoFast.Segmentation.Core.Ingest;

/// <summary>
/// One accepted source format (ingest plan §6).
///
/// <para><see cref="Extension"/> is the canonical one: it is what goes into the S3 key and into
/// the signed POST policy, and it comes from this table rather than from the uploaded filename —
/// a filename is attacker-controlled and a key built from one is a path-traversal surface.</para>
/// </summary>
/// <param name="Extension">Lowercase, with the dot. Unique across the table.</param>
/// <param name="MediaType">The canonical media type; the one the POST policy is signed for.</param>
/// <param name="Label">What the upload page shows a person.</param>
/// <param name="ProducesLayout">True when the format carries page geometry the converter can extract.</param>
/// <param name="OcrCapable">True when OCR may run for this format (always, or when no text layer).</param>
/// <param name="Aliases">
/// Other media types a browser plausibly reports for this extension. Windows sends
/// <c>application/vnd.ms-excel</c> for <c>.csv</c>, Chrome sends nothing at all for <c>.md</c>,
/// and rejecting those would refuse perfectly ordinary uploads.
/// </param>
public sealed record SourceFormat(
    string Extension,
    string MediaType,
    string Label,
    bool ProducesLayout,
    bool OcrCapable,
    IReadOnlyList<string> Aliases)
{
    /// <summary>
    /// False for Markdown and plain text: the source already <em>is</em> the pipeline's input, so
    /// the converter is never called for them (ingest plan C7).
    /// </summary>
    public bool RequiresConversion =>
        !SourceFormats.PassthroughExtensions.Contains(Extension, StringComparer.Ordinal);

    /// <summary>True when <paramref name="mediaType"/> is this format's canonical type or an alias.</summary>
    public bool Accepts(string mediaType) =>
        string.Equals(MediaType, mediaType, StringComparison.OrdinalIgnoreCase)
        || Aliases.Contains(mediaType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The allowlist of ingest plan §6, in one place.
///
/// <para>Three consumers read it and none of them keeps its own copy: <c>api</c> validates
/// <c>CreateUpload</c> against it and derives the key's extension from it, the converter asserts
/// the format again before dispatch (a valid presigned POST can still carry <em>different bytes</em>
/// under an allowed extension), and the browser builds its <c>accept</c> attribute from
/// <c>ListSourceFormats</c> — which is this table over the wire.</para>
///
/// <para>Audio, video, <c>.zip</c> and URL sources are deliberately absent: transcription is
/// either a remote API or a local model, an archive is a decompression-bomb surface, and a URL is
/// an SSRF surface (ingest plan §3, §27).</para>
/// </summary>
public static class SourceFormats
{
    /// <summary>10 MiB. The size of the <em>source</em> upload, before conversion.</summary>
    public const long DefaultMaxBytes = 10L * 1024 * 1024;

    /// <summary>The formats that are already Markdown-shaped, so the converter never sees them.</summary>
    public static readonly IReadOnlySet<string> PassthroughExtensions =
        new HashSet<string>(StringComparer.Ordinal) { ".md", ".markdown", ".txt" };

    /// <summary>
    /// Sent by a browser that could not identify the file. Treated as "no opinion" rather than as
    /// a mismatch: refusing it would reject every <c>.msg</c> and most <c>.epub</c> uploads.
    /// </summary>
    private static readonly IReadOnlySet<string> UnknownMediaTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/octet-stream",
            "binary/octet-stream",
        };

    private static readonly SourceFormat[] Formats =
    [
        // Markdown and text — passthrough. The source is the output.
        new(".md", "text/markdown", "Markdown", false, false, ["text/plain", "text/x-markdown"]),
        new(".markdown", "text/markdown", "Markdown", false, false, ["text/plain", "text/x-markdown"]),
        new(".txt", "text/plain", "Plain text", false, false, ["text/markdown"]),

        // PDF — the layout-bearing format the pipeline was designed around.
        new(".pdf", "application/pdf", "PDF", true, true, ["application/x-pdf"]),

        // Word. .doc (legacy binary) is deliberately absent: unsupported upstream.
        new(
            ".docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "Word",
            false,
            false,
            []),

        new(
            ".pptx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "PowerPoint",
            false,
            false,
            []),

        new(
            ".xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Excel",
            false,
            false,
            []),

        new(".xls", "application/vnd.ms-excel", "Excel (legacy)", false, false, []),

        new(".html", "text/html", "HTML", false, false, ["application/xhtml+xml"]),
        new(".htm", "text/html", "HTML", false, false, ["application/xhtml+xml"]),

        // Windows reports .csv as an Excel type, which is why the alias is here rather than a
        // special case at the call site.
        new(".csv", "text/csv", "CSV", false, false, ["application/csv", "application/vnd.ms-excel", "text/plain"]),
        new(".tsv", "text/tab-separated-values", "TSV", false, false, ["text/plain"]),
        new(".json", "application/json", "JSON", false, false, ["text/json", "text/plain"]),
        new(".xml", "application/xml", "XML", false, false, ["text/xml", "text/plain"]),

        new(".epub", "application/epub+zip", "EPUB", false, false, ["application/epub"]),

        // Outlook item. Attachments are not recursed in v1 (ingest plan §27).
        new(".msg", "application/vnd.ms-outlook", "Outlook message", false, false, ["application/x-msg"]),

        new(".ipynb", "application/x-ipynb+json", "Notebook", false, false, ["application/json", "text/plain"]),

        // Images. Text comes from OCR, layout comes from the OCR boxes — so both flags are true.
        new(".png", "image/png", "PNG image", true, true, []),
        new(".jpg", "image/jpeg", "JPEG image", true, true, ["image/jpg"]),
        new(".jpeg", "image/jpeg", "JPEG image", true, true, ["image/jpg"]),
        new(".tif", "image/tiff", "TIFF image", true, true, ["image/tif"]),
        new(".tiff", "image/tiff", "TIFF image", true, true, ["image/tif"]),
        new(".bmp", "image/bmp", "Bitmap image", true, true, ["image/x-ms-bmp"]),
    ];

    private static readonly Dictionary<string, SourceFormat> ByExtensionMap =
        Formats.ToDictionary(f => f.Extension, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SourceFormat> All => Formats;

    /// <summary>The <c>accept</c> attribute the upload input carries, built from the table.</summary>
    public static string AcceptAttribute =>
        string.Join(
            ',',
            Formats.Select(f => f.Extension)
                .Concat(Formats.Select(f => f.MediaType).Distinct(StringComparer.Ordinal)));

    /// <summary>
    /// Resolves an upload by extension <em>and</em> media type (ingest plan C1).
    ///
    /// <para>The extension decides which format this is; the media type only has to agree with it.
    /// A browser that reports nothing, or reports <c>application/octet-stream</c>, is taken as
    /// having no opinion — that is the common case for <c>.msg</c> and <c>.epub</c> — but a media
    /// type that names a <em>different</em> format is a mismatch and is refused, because the two
    /// together are what the POST policy is signed for.</para>
    /// </summary>
    public static bool TryResolve(string? fileName, string? mediaType, out SourceFormat format)
    {
        format = null!;

        var extension = ExtensionOf(fileName);
        if (extension is null || !ByExtensionMap.TryGetValue(extension, out var candidate))
        {
            return false;
        }

        var declared = mediaType?.Trim();

        // A browser may append parameters ("text/plain; charset=utf-8"); only the type matters.
        var semicolon = declared?.IndexOf(';');
        if (semicolon is > 0)
        {
            declared = declared![..semicolon.Value].TrimEnd();
        }

        if (!string.IsNullOrEmpty(declared)
            && !UnknownMediaTypes.Contains(declared)
            && !candidate.Accepts(declared))
        {
            return false;
        }

        format = candidate;
        return true;
    }

    /// <summary>
    /// The extension a filename ends in, lowercased, or null when it has none.
    ///
    /// <para>Only the last path segment is considered, so <c>../../etc/passwd.pdf</c> resolves the
    /// same way <c>passwd.pdf</c> does — and the key is then built from <see cref="SourceFormat.Extension"/>
    /// anyway, never from this string.</para>
    /// </summary>
    public static string? ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = fileName.AsSpan().TrimEnd();
        var lastSeparator = name.LastIndexOfAny('/', '\\');
        if (lastSeparator >= 0)
        {
            name = name[(lastSeparator + 1)..];
        }

        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1)
        {
            return null;
        }

        return name[dot..].ToString().ToLowerInvariant();
    }

    /// <summary>The name to show for a rejected upload: ".xyz", or "that" when there is no extension.</summary>
    public static string DescribeRejected(string? fileName) => ExtensionOf(fileName) ?? "that kind of";
}
