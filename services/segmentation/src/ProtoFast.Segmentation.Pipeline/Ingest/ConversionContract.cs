using System.Text.Json.Serialization;

namespace ProtoFast.Segmentation.Pipeline.Ingest;

/// <summary>
/// The body of <c>POST /convert</c> (ingest plan §9).
///
/// <para>Only keys cross the wire, never bytes: the converter reads the source from S3 and writes
/// the Markdown, the layout and the report back to S3 itself. That is what keeps a 10 MiB document
/// out of the worker's memory, and what lets the converter be moved behind a queue later without
/// changing either side of this contract.</para>
/// </summary>
public sealed record ConversionRequest
{
    [JsonPropertyName("uploadId")]
    public required string UploadId { get; init; }

    [JsonPropertyName("sourceKey")]
    public required string SourceKey { get; init; }

    [JsonPropertyName("markdownKey")]
    public required string MarkdownKey { get; init; }

    [JsonPropertyName("layoutKey")]
    public required string LayoutKey { get; init; }

    [JsonPropertyName("reportKey")]
    public required string ReportKey { get; init; }

    [JsonPropertyName("mediaType")]
    public required string MediaType { get; init; }

    /// <summary>Only ever used for the report and for error messages; never for the key.</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("ocr")]
    public required OcrRequest Ocr { get; init; }

    /// <summary>W3C trace context, so the converter's spans join the run's own trace.</summary>
    [JsonPropertyName("traceparent")]
    public string? Traceparent { get; init; }
}

public sealed record OcrRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("languages")]
    public IReadOnlyList<string> Languages { get; init; } = ["eng"];

    [JsonPropertyName("maxPages")]
    public int MaxPages { get; init; } = 200;
}

/// <summary>A successful conversion. <see cref="LayoutKey"/> is empty when the format carries no geometry.</summary>
public sealed record ConversionResult
{
    [JsonPropertyName("markdownKey")]
    public string MarkdownKey { get; init; } = string.Empty;

    [JsonPropertyName("layoutKey")]
    public string LayoutKey { get; init; } = string.Empty;

    [JsonPropertyName("markdownBytes")]
    public long MarkdownBytes { get; init; }

    [JsonPropertyName("pages")]
    public int Pages { get; init; }

    [JsonPropertyName("producer")]
    public string Producer { get; init; } = string.Empty;

    [JsonPropertyName("ocr")]
    public OcrResult? Ocr { get; init; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    public bool HasLayout => !string.IsNullOrEmpty(LayoutKey);
}

public sealed record OcrResult
{
    [JsonPropertyName("applied")]
    public bool Applied { get; init; }

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = string.Empty;

    [JsonPropertyName("pagesOcred")]
    public int PagesOcred { get; init; }

    [JsonPropertyName("meanConfidence")]
    public double MeanConfidence { get; init; }
}

/// <summary>
/// The error body. <c>code</c> is what the caller branches on; <c>message</c> is written to be
/// shown to the person who uploaded the document.
/// </summary>
public sealed record ConversionProblem
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}
