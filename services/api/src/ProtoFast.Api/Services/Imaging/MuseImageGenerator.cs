using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProtoFast.Api.Services.Directing;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Meta's Muse Image through the Responses API. Muse is itself the orchestrator: it reasons over the
/// captioned references it is handed and decides what to take from each, so the caller only
/// gathers them and says what each one is.
/// </summary>
public sealed class MuseImageGenerator(MuseImageOptions options, HttpClient http) : IImageGenerator
{
    public const string HttpClientName = "imaging-muse";

    public string Provider => ImageProviders.Meta;

    public string Model => options.Model;

    public async Task<GeneratedImage> GenerateAsync(ImageRequest request, CancellationToken ct)
    {
        var content = new JsonArray();
        void Text(string text)
        {
            if (text.Length > 0)
            {
                content.Add(new JsonObject { ["type"] = "input_text", ["text"] = text });
            }
        }

        Text(request.Preamble);
        foreach (var reference in request.References)
        {
            Text(reference.Caption);
            content.Add(new JsonObject
            {
                ["type"] = "input_image",
                ["image_url"] = $"data:{reference.ContentType};base64,{Convert.ToBase64String(reference.Bytes)}",
            });
        }

        Text(request.Prompt);
        var tool = new JsonObject
        {
            ["type"] = "image_generation",
            ["size"] = request.Size,
            ["output_format"] = options.OutputFormat,
            ["enable_image_search"] = options.EnableImageSearch,
            ["enable_web_search"] = false,
            ["enable_shell"] = false,
        };
        if (options.ReasoningStrength.Length > 0)
        {
            tool["reasoning_strength"] = options.ReasoningStrength;
        }

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["store"] = false,
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            ["tools"] = new JsonArray(tool),
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/responses")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Authorization = new("Bearer", options.ApiKey is { Length: > 0 } key
            ? key
            : throw new ImageException("No Meta API key is configured (Providers:Meta:ApiKey)."));
        using var response = await SendAsync(message, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var output = ModelJson.Array(json.RootElement, "output").ToList();
        var image = output
            .Where(o => ModelJson.Str(o, "type") == "image_generation_call")
            .Select(o => ModelJson.Str(o, "result"))
            .FirstOrDefault(r => !string.IsNullOrEmpty(r));
        if (image is null)
        {
            var refusal = output
                .Where(o => ModelJson.Str(o, "type") == "message")
                .SelectMany(o => ModelJson.Array(o, "content"))
                .Select(c => ModelJson.Str(c, "text"))
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            throw new ImageException(refusal is null ? "Muse answered without a picture." : $"Muse made no picture: {refusal.Trim()}");
        }

        var notes = string.Join('\n', output
            .Where(o => ModelJson.Str(o, "type") == "reasoning")
            .SelectMany(o => ModelJson.Array(o, "summary"))
            .Select(s => ModelJson.Str(s, "text"))
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        var root = json.RootElement;
        var usage = root.TryGetProperty("usage", out var u) ? u : default;
        var details = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens_details", out var d) ? d : default;
        return new GeneratedImage(
            Convert.FromBase64String(image),
            $"image/{options.OutputFormat}",
            (long)(options.PricePerImage * 1_000_000m),
            notes.Length > 0 ? notes : null,
            new ImageUsage(
                ModelJson.Str(root, "id"),
                ModelJson.Str(root, "model"),
                ModelJson.Str(root, "status"),
                ModelJson.Int(usage, "input_tokens"),
                ModelJson.Int(details, "cached_tokens"),
                ModelJson.Int(usage, "output_tokens")));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException e)
        {
            throw new ImageException("Muse couldn't be reached.", e);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw new ImageException($"Muse refused the picture ({(int)response.StatusCode}): {(detail.Length <= 200 ? detail : detail[..200] + "…")}");
        }

        return response;
    }
}
