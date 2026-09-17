using System.Text.Json.Serialization;

namespace ProtoFast.Segmentation.Core.Ingest;

/// <summary>
/// The raw per-line layout the upstream converter emits, in <em>document</em> units (points,
/// page fractions) rather than the normalized ones the pipeline uses. Phase 0 turns these into
/// <c>LayoutFeatures</c> once the document's own statistics are known (plan §9.2).
/// </summary>
public sealed record RawLayoutLine
{
    /// <summary>1-based page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; init; } = 1;

    /// <summary>The converter's text for this line. Used to align against the Markdown lines.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; init; }

    [JsonPropertyName("y")]
    public double Y { get; init; }

    [JsonPropertyName("width")]
    public double Width { get; init; }

    [JsonPropertyName("height")]
    public double Height { get; init; }

    [JsonPropertyName("pageWidth")]
    public double PageWidth { get; init; } = 1;

    [JsonPropertyName("pageHeight")]
    public double PageHeight { get; init; } = 1;

    [JsonPropertyName("fontSize")]
    public double FontSize { get; init; }

    [JsonPropertyName("bold")]
    public bool Bold { get; init; }

    [JsonPropertyName("italic")]
    public bool Italic { get; init; }

    [JsonPropertyName("column")]
    public int Column { get; init; }
}

/// <summary>The whole <c>.layout.json</c> sibling of an upload; absent for plain Markdown.</summary>
public sealed record LayoutDocument
{
    [JsonPropertyName("producer")]
    public string? Producer { get; init; }

    [JsonPropertyName("lines")]
    public IReadOnlyList<RawLayoutLine> Lines { get; init; } = [];
}
