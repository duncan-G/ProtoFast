using System.Diagnostics.CodeAnalysis;

namespace Protofast.DocumentImport.Core;

public static class SourceFormats
{
    // 10MiB
    public const long DefaultMaxBytes = 10L * 1024 * 1024;

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

    // Columns: extension, media type, label, produces layout, OCR capable, requires conversion, media-type aliases.
    private static readonly SourceFormat[] Formats =
    [
        // Markdown and text are already Markdown-shaped, so the source is the output and no
        // conversion runs — the only rows with RequiresConversion: false.
        new(".md", "text/markdown", "Markdown", false, false, false, ["text/plain", "text/x-markdown"]),
        new(".markdown", "text/markdown", "Markdown", false, false, false, ["text/plain", "text/x-markdown"]),
        new(".txt", "text/plain", "Plain text", false, false, false, ["text/markdown"]),

        // PDF — the layout-bearing format the pipeline was designed around.
        new(".pdf", "application/pdf", "PDF", true, true, true, ["application/x-pdf"]),

        // Word. .doc (legacy binary) is deliberately absent: unsupported upstream.
        new(
            ".docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "Word",
            false,
            false,
            true,
            []),

        new(
            ".pptx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "PowerPoint",
            false,
            false,
            true,
            []),

        new(
            ".xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Excel",
            false,
            false,
            true,
            []),

        new(".xls", "application/vnd.ms-excel", "Excel (legacy)", false, false, true, []),

        new(".html", "text/html", "HTML", false, false, true, ["application/xhtml+xml"]),
        new(".htm", "text/html", "HTML", false, false, true, ["application/xhtml+xml"]),

        // Windows reports .csv as an Excel type, which is why the alias is here rather than a
        // special case at the call site.
        new(".csv", "text/csv", "CSV", false, false, true, ["application/csv", "application/vnd.ms-excel", "text/plain"]),
        new(".tsv", "text/tab-separated-values", "TSV", false, false, true, ["text/plain"]),
        new(".json", "application/json", "JSON", false, false, true, ["text/json", "text/plain"]),
        new(".xml", "application/xml", "XML", false, false, true, ["text/xml", "text/plain"]),

        new(".epub", "application/epub+zip", "EPUB", false, false, true, ["application/epub"]),

        // Outlook item. Attachments are not recursed in v1 (ingest plan §27).
        new(".msg", "application/vnd.ms-outlook", "Outlook message", false, false, true, ["application/x-msg"]),

        new(".ipynb", "application/x-ipynb+json", "Notebook", false, false, true, ["application/json", "text/plain"]),

        // Images. Text comes from OCR, layout comes from the OCR boxes — so both flags are true.
        new(".png", "image/png", "PNG image", true, true, true, []),
        new(".jpg", "image/jpeg", "JPEG image", true, true, true, ["image/jpg"]),
        new(".jpeg", "image/jpeg", "JPEG image", true, true, true, ["image/jpg"]),
        new(".tif", "image/tiff", "TIFF image", true, true, true, ["image/tif"]),
        new(".tiff", "image/tiff", "TIFF image", true, true, true, ["image/tif"]),
        new(".bmp", "image/bmp", "Bitmap image", true, true, true, ["image/x-ms-bmp"]),
    ];

    private static readonly Dictionary<string, SourceFormat> ByExtensionMap =
        Formats.ToDictionary(f => f.Extension, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SourceFormat> All => Formats;

    /// <summary>The <c>accept</c> attribute the upload input carries, built from the table.</summary>
    public static readonly string AcceptAttribute =
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
    public static bool TryResolve(string? fileName, string? mediaType, [NotNullWhen(true)] out SourceFormat? format)
    {
        format = null;

        var extension = ExtensionOf(fileName);
        if (extension is null || !ByExtensionMap.TryGetValue(extension, out var candidate))
        {
            return false;
        }

        var declared = mediaType?.Trim();

        // A browser may append parameters ("text/plain; charset=utf-8"); only the type matters.
        if (declared is not null)
        {
            var semicolon = declared.IndexOf(';');
            if (semicolon > 0)
            {
                declared = declared[..semicolon].TrimEnd();
            }
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
