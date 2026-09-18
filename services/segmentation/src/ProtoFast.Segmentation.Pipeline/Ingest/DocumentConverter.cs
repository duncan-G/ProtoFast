using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Observability;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Ingest;

/// <summary>
/// The worker's half of the conversion contract (ingest plan §9.1).
///
/// <para>The split between permanent (4xx) and transient (5xx) is the whole point of this class.
/// A malformed PDF fails the run immediately; an unreachable or restarting converter gets one
/// retry and then leaves the message on the queue, so runs pile up across a deploy instead of
/// dying in it.</para>
/// </summary>
public sealed class DocumentConverter(
    HttpClient http,
    IOptions<ConversionOptions> options,
    ILogger<DocumentConverter> logger) : IDocumentConverter
{
    /// <summary>Named so conversion shows up on the run's own trace rather than as an orphan.</summary>
    public static readonly ActivitySource ActivitySource = new("ProtoFast.Segmentation.Conversion");

    private readonly ConversionOptions _options = options.Value;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// The OCR policy to ask for, from configuration. It travels in the request rather than living
    /// in the converter's own environment so that changing it is a worker restart, not an image
    /// rebuild — and so a run's report records what was actually asked for.
    /// </summary>
    public OcrRequest OcrRequest => new()
    {
        Enabled = _options.OcrEnabled,
        Languages = _options.OcrLanguages.Count > 0 ? _options.OcrLanguages : ["eng"],
        MaxPages = _options.MaxOcrPages,
    };

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            throw new PipelineFailureException(
                PipelinePhase.Ingest,
                $"This document is a {request.MediaType} and needs converting, but no converter is configured "
                + "for this environment. Upload Markdown instead, or set Seg_Conversion__Endpoint.",
                permanent: true);
        }

        using var activity = ActivitySource.StartActivity("conversion.convert", ActivityKind.Client);
        activity?.SetTag("media_type", request.MediaType);
        activity?.SetTag("upload.id", request.UploadId);

        try
        {
            // One retry, and only on a transient failure. Two attempts is what covers a converter
            // mid-restart; a third would just spend more of the SQS visibility window on a
            // converter that is plainly not there, and the message returns to the queue either way.
            var result = await AttemptAsync(request, attempt: 1, ct)
                ?? await AttemptAsync(request, attempt: 2, ct)
                ?? throw new PipelineFailureException(
                    PipelinePhase.Ingest,
                    "The document converter is not responding. The run will resume when it is back.");

            activity?.SetTag("pages", result.Pages);
            activity?.SetTag("markdown_bytes", result.MarkdownBytes);
            activity?.SetTag("ocr.applied", result.Ocr?.Applied ?? false);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Every throw above is a refusal or an exhausted retry — a failed conversion, and the
            // span has to say so. Cancellation is excluded: a worker shutting down mid-convert is
            // not a converter fault, and colouring it red would make every deploy look like an
            // outage.
            activity.Fail(ex);
            throw;
        }
    }

    /// <summary>
    /// One attempt. Returns null when the failure is transient and worth retrying, and throws when
    /// it is not — which keeps the retry decision in one place rather than spread across catch
    /// blocks at the call site.
    /// </summary>
    private async Task<ConversionResult?> AttemptAsync(ConversionRequest request, int attempt, CancellationToken ct)
    {
        HttpResponseMessage response;

        try
        {
            response = await http.PostAsJsonAsync("convert", request, Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Connection refused, DNS failure, or the client timeout tripped before the converter's
            // own 8-minute budget did. All three are "try again".
            logger.LogWarning(
                ex, "Conversion attempt {Attempt} for upload {UploadId} could not reach the converter.",
                attempt, request.UploadId);
            return null;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ConversionResult>(Json, ct)
                    ?? throw new PipelineFailureException(
                        PipelinePhase.Ingest, "The converter returned an empty result.", permanent: true);
            }

            var problem = await ReadProblemAsync(response, ct);

            // 4xx is the converter saying this document will never convert. Failing now is what
            // stops one malformed file from spending five delivery attempts on its way to a DLQ
            // that is supposed to mean "something is wrong with the system".
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                logger.LogWarning(
                    "Conversion of upload {UploadId} was refused ({Status} {Code}): {Message}",
                    request.UploadId, (int)response.StatusCode, problem.Code, problem.Message);

                throw new PipelineFailureException(PipelinePhase.Ingest, problem.Message, permanent: true);
            }

            logger.LogWarning(
                "Conversion attempt {Attempt} for upload {UploadId} failed ({Status} {Code}): {Message}",
                attempt, request.UploadId, (int)response.StatusCode, problem.Code, problem.Message);

            // A timeout that survives its retry is not worth a third: say so in the run's error
            // rather than leaving the user with a document that silently never finishes.
            if (attempt > 1 && response.StatusCode == HttpStatusCode.GatewayTimeout)
            {
                throw new PipelineFailureException(
                    PipelinePhase.Ingest,
                    "This document takes too long to convert. Try splitting it or exporting a smaller version.",
                    permanent: true);
            }

            return null;
        }
    }

    /// <summary>
    /// The converter's structured error, or a generic one when the body is not the shape this
    /// contract promises — a 502 from something in front of it, for instance.
    /// </summary>
    private static async Task<ConversionProblem> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ConversionProblem>(Json, ct) ?? Generic(response);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return Generic(response);
        }
    }

    private static ConversionProblem Generic(HttpResponseMessage response) => new()
    {
        Code = "conversion_failed",
        Message = $"The document could not be converted (HTTP {(int)response.StatusCode}).",
    };
}

/// <summary>
/// Phase 0's view of the converter. An interface purely so the integration tests can assert that a
/// Markdown upload never calls it (ingest plan §24).
/// </summary>
public interface IDocumentConverter
{
    /// <summary>False when no endpoint is configured, which is the state a fresh clone starts in.</summary>
    bool IsConfigured { get; }

    /// <summary>The configured OCR policy, sent with every request.</summary>
    OcrRequest OcrRequest { get; }

    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct);
}
