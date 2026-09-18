using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Pipeline.Ingest;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// A converter that writes what it was told to write and counts how often it was asked.
///
/// <para>The count is the assertion: a Markdown upload has to reach <c>publish</c> with
/// <see cref="Calls"/> still zero, and a re-run from phase 0 must not convert a second time
/// (ingest plan §24, C10). It writes through the artifact store rather than returning bytes
/// because that is what the real converter does — the worker never sees the Markdown cross the
/// wire, it reads it back from S3.</para>
/// </summary>
public sealed class StubDocumentConverter : IDocumentConverter
{
    private readonly Dictionary<string, (string Markdown, InMemoryArtifactStore Store)> _outputs = [];

    /// <summary>Requests received. Zero is the expected value for every passthrough upload.</summary>
    public List<ConversionRequest> Calls { get; } = [];

    /// <summary>Set to fail the next conversion, standing in for an unreadable document.</summary>
    public Exception? Fail { get; set; }

    /// <summary>Emitted alongside the Markdown when set, so the layout-joining path is exercised.</summary>
    public LayoutDocument? Layout { get; set; }

    public bool IsConfigured => true;

    public OcrRequest OcrRequest { get; } = new() { Enabled = true, Languages = ["eng"], MaxPages = 200 };

    /// <summary>Arranges what this converter will write when it is asked for <paramref name="markdownKey"/>.</summary>
    public void Produce(string markdownKey, string markdown, InMemoryArtifactStore store) =>
        _outputs[markdownKey] = (markdown, store);

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        Calls.Add(request);

        if (Fail is not null)
        {
            throw Fail;
        }

        if (!_outputs.TryGetValue(request.MarkdownKey, out var output))
        {
            throw new InvalidOperationException(
                $"The test did not arrange any output for '{request.MarkdownKey}'.");
        }

        await output.Store.WriteTextAsync(
            request.MarkdownKey, output.Markdown, "conversion", "text/markdown", ct);

        await output.Store.WriteAsync(
            request.ReportKey,
            new { uploadId = request.UploadId, mediaType = request.MediaType, pages = 1 },
            "conversion",
            ct);

        if (Layout is not null)
        {
            await output.Store.WriteAsync(request.LayoutKey, Layout, "conversion", ct);
        }

        return new ConversionResult
        {
            MarkdownKey = request.MarkdownKey,
            LayoutKey = Layout is null ? string.Empty : request.LayoutKey,
            MarkdownBytes = output.Markdown.Length,
            Pages = 1,
            Producer = "stub",
            Ocr = new OcrResult { Applied = false, Engine = "none" },
            DurationMs = 1,
        };
    }
}
