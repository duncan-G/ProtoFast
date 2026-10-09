using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoFast.Api.Services.Directing;

namespace ProtoFast.Api.Services.Speech;

/// <summary>
/// The OpenAI <c>/v1/audio/speech</c> API, as vLLM-Omni serves Qwen3-TTS. Delivery tags become the
/// request's style instructions, since the text is read as written.
/// </summary>
public sealed class OpenAiSpeechSynthesizer(OpenAiSpeechOptions options, HttpClient http) : ISpeechSynthesizer
{
    public const string HttpClientName = "speech-openai";

    public string Provider => SpeechProviders.OpenAi;

    public string Model => options.Model;

    public SpeechRequest Prepare(string performance, string? voiceId)
    {
        var tags = Performances.Tags(performance);
        return new SpeechRequest(
            Performances.Strip(performance),
            tags.Count == 0 ? null : string.Join(", ", tags),
            string.IsNullOrWhiteSpace(voiceId) ? options.DefaultVoice : voiceId.Trim());
    }

    public async Task<SpokenLine> SpeakAsync(SpeechRequest request, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["input"] = request.Text,
            ["voice"] = request.Voice,
            ["response_format"] = options.ResponseFormat,
        };
        if (request.Instructions is not null)
        {
            body["instructions"] = request.Instructions;
        }

        if (options.Language is not null)
        {
            body["language"] = options.Language;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl()}/audio/speech") { Content = JsonContent.Create(body) };
        Authorize(message);
        using var response = await SendAsync(message, ct);
        var audio = await response.Content.ReadAsByteArrayAsync(ct);
        if (audio.Length == 0)
        {
            throw new SpeechException("The speech server returned no audio.");
        }

        return new SpokenLine(audio, response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg", 0);
    }

    /// <summary>vLLM-Omni lists under <c>/audio/voices</c>; other servers under <c>/voices</c>.</summary>
    public async Task<IReadOnlyList<SpeechVoice>> ListVoicesAsync(CancellationToken ct)
    {
        foreach (var path in new[] { "/audio/voices", "/voices" })
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, BaseUrl() + path);
            Authorize(message);
            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var voices = Voices(json.RootElement);
            if (voices.Count > 0)
            {
                return voices;
            }
        }

        return [new SpeechVoice(options.DefaultVoice, options.DefaultVoice)];
    }

    private static List<SpeechVoice> Voices(JsonElement root)
    {
        var list = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("voices", out var v) ? v
            : root.TryGetProperty("data", out var d) ? d
            : default;
        if (list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return list.EnumerateArray()
            .Select(e => e.ValueKind switch
            {
                JsonValueKind.String => new SpeechVoice(e.GetString()!, e.GetString()!),
                JsonValueKind.Object => new SpeechVoice(
                    ModelJson.Str(e, "id") ?? ModelJson.Str(e, "voice_id") ?? ModelJson.Str(e, "name") ?? "",
                    ModelJson.Str(e, "name") ?? ModelJson.Str(e, "id") ?? ""),
                _ => new SpeechVoice("", ""),
            })
            .Where(voice => voice.Id.Length > 0)
            .ToList();
    }

    private string BaseUrl() =>
        options.BaseUrl?.TrimEnd('/') ?? throw new SpeechException("No speech server is configured (Speech:OpenAi:BaseUrl).");

    private void Authorize(HttpRequestMessage message)
    {
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
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
            throw new SpeechException("The speech server couldn't be reached.", e);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw new SpeechException($"The speech server refused the line ({(int)response.StatusCode}): {Shorten(detail)}");
        }

        return response;
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
