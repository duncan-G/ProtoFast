using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>Meta's Muse Image through the Responses API.</summary>
public sealed class MuseImageGenerator(MuseImageOptions options, HttpClient http) : IImageGenerator
{
    public async Task<GeneratedImage> GenerateAsync(string prompt, IReadOnlyList<ImageInput> references, CancellationToken ct)
    {
        var key = options.ApiKey is { Length: > 0 } k
            ? k
            : throw new ImageException("No Meta API key is configured (Providers:Meta:ApiKey).");
        var content = new JsonArray();
        foreach (var reference in references)
        {
            content.Add(new JsonObject { ["type"] = "input_text", ["text"] = reference.Caption });
            content.Add(new JsonObject
            {
                ["type"] = "input_image",
                ["image_url"] = $"data:{reference.ContentType};base64,{Convert.ToBase64String(reference.Bytes)}",
            });
        }

        content.Add(new JsonObject { ["type"] = "input_text", ["text"] = prompt });
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["store"] = false,
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            // The search tools default to on; a story's world is its own.
            ["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = "image_generation",
                ["size"] = options.Size,
                ["output_format"] = options.OutputFormat,
                ["enable_image_search"] = false,
                ["enable_web_search"] = false,
                ["enable_shell"] = false,
            }),
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/responses")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Authorization = new("Bearer", key);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException e)
        {
            throw new ImageException("Muse couldn't be reached.", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                throw new ImageException($"Muse refused the picture ({(int)response.StatusCode}): {(detail.Length <= 200 ? detail : detail[..200] + "…")}");
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var output = Items(json.RootElement, "output").ToList();
            var image = output
                .Where(o => Str(o, "type") == "image_generation_call")
                .Select(o => Str(o, "result"))
                .FirstOrDefault(r => !string.IsNullOrEmpty(r));
            if (image is null)
            {
                var refusal = output
                    .Where(o => Str(o, "type") == "message")
                    .SelectMany(o => Items(o, "content"))
                    .Select(c => Str(c, "text"))
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                throw new ImageException(refusal is null ? "Muse answered without a picture." : $"Muse made no picture: {refusal.Trim()}");
            }

            var notes = string.Join('\n', output
                .Where(o => Str(o, "type") == "reasoning")
                .SelectMany(o => Items(o, "summary"))
                .Select(s => Str(s, "text"))
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
                    Str(root, "id"),
                    Str(root, "model"),
                    Str(root, "status"),
                    Int(usage, "input_tokens"),
                    Int(details, "cached_tokens"),
                    Int(usage, "output_tokens")));
        }
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static long? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
