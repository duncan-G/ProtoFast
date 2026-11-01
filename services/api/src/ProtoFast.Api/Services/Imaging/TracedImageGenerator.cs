using System.Diagnostics;
using System.Text.Json;
using ProtoFast.DocumentImport.Core;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Wraps an image call in an OpenTelemetry GenAI <c>generate_content</c> client span, like the
/// worker's <c>TracedLanguageModel</c>. The prompt, the model's reasoning and the picture are
/// recorded only when content capture is on.
/// </summary>
public sealed class TracedImageGenerator(
    IImageGenerator inner, string providerName, string model, string serverAddress, bool captureContent) : IImageGenerator
{
    private const string Operation = "generate_content";

    // Base64'd, well under the 4 MiB an OTLP gRPC export may carry.
    private const int PictureBudgetBytes = 1024 * 1024;

    public async Task<GeneratedImage> GenerateAsync(string prompt, IReadOnlyList<ImageInput> references, CancellationToken ct)
    {
        using var activity = ImagingTelemetry.Source.StartActivity($"{Operation} {model}", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("gen_ai.operation.name", Operation);
            activity.SetTag("gen_ai.provider.name", providerName);
            activity.SetTag("gen_ai.request.model", model);
            activity.SetTag("gen_ai.output.type", "image");
            activity.SetTag("server.address", serverAddress);
            activity.SetTag("server.port", 443);
        }

        // The picture made gets the media budget first, then the references.
        var media = new GenAiMedia(PictureBudgetBytes);
        try
        {
            var image = await inner.GenerateAsync(prompt, references, ct);
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag("gen_ai.response.model", image.Usage?.ResponseModel ?? model);
                if (image.Usage is { } usage)
                {
                    activity.SetTag("gen_ai.response.id", usage.ResponseId);
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
                            parts = OutputParts(image, media),
                            finish_reason = image.Usage?.Status ?? "stop",
                        },
                    ]));
                }
            }

            return image;
        }
        catch (Exception e)
        {
            activity.Fail(e);
            throw;
        }
        finally
        {
            if (activity is { IsAllDataRequested: true } && captureContent)
            {
                activity.SetTag("gen_ai.input.messages", Json([new { role = "user", parts = InputParts(prompt, references, media) }]));
            }
        }
    }

    private static object[] InputParts(string prompt, IReadOnlyList<ImageInput> references, GenAiMedia media) =>
    [
        .. references.SelectMany(r => new object[]
        {
            new { type = "text", content = r.Caption },
            media.Part("image", r.Bytes, r.ContentType, r.Uri),
        }),
        new { type = "text", content = prompt },
    ];

    private static object[] OutputParts(GeneratedImage image, GenAiMedia media) =>
    [
        .. string.IsNullOrEmpty(image.Notes) ? [] : new object[] { new { type = "reasoning", content = image.Notes } },
        media.Part("image", image.Bytes, image.ContentType),
    ];

    private static string Json(object[] value) => JsonSerializer.Serialize(value);
}
