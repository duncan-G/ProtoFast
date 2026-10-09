using System.Diagnostics;
using System.Text.Json;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Wraps an image call in an OpenTelemetry GenAI <c>generate_content</c> client span, like the
/// language models' <c>TracedLanguageModel</c>. Captions, the prompt and the model's reasoning are
/// recorded only when content capture is on; images are named by where they are stored, never
/// carried in the span.
/// </summary>
public sealed class TracedImageGenerator(IImageGenerator inner, string serverAddress, bool captureContent) : IImageGenerator
{
    private const string Operation = "generate_content";

    public string Provider => inner.Provider;

    public string Model => inner.Model;

    public async Task<GeneratedImage> GenerateAsync(ImageRequest request, CancellationToken ct)
    {
        using var activity = ImagingTelemetry.Source.StartActivity($"{Operation} {inner.Model}", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("gen_ai.operation.name", Operation);
            activity.SetTag("gen_ai.provider.name", inner.Provider);
            activity.SetTag("gen_ai.request.model", inner.Model);
            activity.SetTag("gen_ai.output.type", "image");
            activity.SetTag("server.address", serverAddress);
            activity.SetTag("server.port", 443);
            activity.SetTag("protofast.image.size", request.Size);
            activity.SetTag("protofast.image.references", request.References.Count);
            if (captureContent)
            {
                activity.SetTag("gen_ai.input.messages", Json([new { role = "user", parts = InputParts(request) }]));
            }
        }

        try
        {
            var image = await inner.GenerateAsync(request, ct);
            if (activity is { IsAllDataRequested: true })
            {
                if (image.Usage is { } usage)
                {
                    activity.SetTag("gen_ai.response.id", usage.ResponseId);
                    activity.SetTag("gen_ai.response.model", usage.ResponseModel ?? inner.Model);
                    activity.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
                    activity.SetTag("gen_ai.usage.cache_read.input_tokens", usage.CachedInputTokens);
                    activity.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
                    if (usage.Status is { } status)
                    {
                        activity.SetTag("gen_ai.response.finish_reasons", new[] { status });
                    }
                }

                if (captureContent)
                {
                    activity.SetTag("gen_ai.output.messages", Json(
                    [
                        new
                        {
                            role = "assistant",
                            parts = OutputParts(image),
                            finish_reason = image.Usage?.Status ?? "stop",
                        },
                    ]));
                }
            }

            return image;
        }
        catch (Exception e)
        {
            activity?.SetTag("error.type", e.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, e.Message);
            activity?.AddException(e);
            throw;
        }
    }

    private static object[] InputParts(ImageRequest request) =>
    [
        .. Text(request.Preamble),
        .. request.References.SelectMany(r => (object[])
        [
            .. Text(r.Caption),
            new { type = "uri", modality = "image", mime_type = r.ContentType, uri = r.Uri },
        ]),
        .. Text(request.Prompt),
    ];

    private static object[] OutputParts(GeneratedImage image) =>
    [
        .. string.IsNullOrEmpty(image.Notes) ? [] : new object[] { new { type = "reasoning", content = image.Notes } },
        new { type = "image", modality = "image", mime_type = image.ContentType, size = image.Bytes.Length },
    ];

    private static object[] Text(string content) =>
        string.IsNullOrEmpty(content) ? [] : [new { type = "text", content }];

    private static string Json(object[] value) => JsonSerializer.Serialize(value);
}
