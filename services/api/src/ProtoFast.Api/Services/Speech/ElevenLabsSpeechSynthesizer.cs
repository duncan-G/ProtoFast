using System.Net.Http.Json;
using System.Text.Json;
using ProtoFast.Api.Services.Directing;

namespace ProtoFast.Api.Services.Speech;

/// <summary>ElevenLabs text to speech. Eleven v3 performs the delivery tags in the text; older models get the words alone.</summary>
public sealed class ElevenLabsSpeechSynthesizer(ElevenLabsSpeechOptions options, HttpClient http) : ISpeechSynthesizer
{
    public const string HttpClientName = "speech-elevenlabs";

    public string Provider => SpeechProviders.ElevenLabs;

    public string Model => options.Model;

    public SpeechRequest Prepare(string performance, string? voiceId)
    {
        var voice = string.IsNullOrWhiteSpace(voiceId) ? options.DefaultVoiceId : voiceId.Trim();
        if (voice.Length == 0)
        {
            throw new SpeechException("Cast a voice first: this speaker has no voice id and no default voice is configured.");
        }

        var performsTags = options.Model.StartsWith("eleven_v3", StringComparison.OrdinalIgnoreCase);
        return new SpeechRequest(performsTags ? Performances.Collapse(performance) : Performances.Strip(performance), null, voice);
    }

    public async Task<SpokenLine> SpeakAsync(SpeechRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{options.BaseUrl.TrimEnd('/')}/v1/text-to-speech/{Uri.EscapeDataString(request.Voice)}?output_format={Uri.EscapeDataString(options.OutputFormat)}")
        {
            Content = JsonContent.Create(new { text = request.Text, model_id = options.Model }),
        };
        Authorize(message);
        using var response = await SendAsync(message, ct);
        var audio = await response.Content.ReadAsByteArrayAsync(ct);
        var cost = (long)(request.Text.Length / 1000m * options.PricePerThousandCharacters * 1_000_000m);
        return new SpokenLine(audio, response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg", cost);
    }

    public async Task<IReadOnlyList<SpeechVoice>> ListVoicesAsync(CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"{options.BaseUrl.TrimEnd('/')}/v1/voices");
        Authorize(message);
        using var response = await SendAsync(message, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.TryGetProperty("voices", out var voices) && voices.ValueKind == JsonValueKind.Array
            ? voices.EnumerateArray()
                .Select(v => new SpeechVoice(ModelJson.Str(v, "voice_id") ?? "", ModelJson.Str(v, "name") ?? ""))
                .Where(v => v.Id.Length > 0)
                .ToList()
            : [];
    }

    private void Authorize(HttpRequestMessage message) =>
        message.Headers.Add("xi-api-key", options.ApiKey ?? throw new SpeechException("No ElevenLabs API key is configured (Speech:ElevenLabs:ApiKey)."));

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException e)
        {
            throw new SpeechException("ElevenLabs couldn't be reached.", e);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw new SpeechException($"ElevenLabs refused the line ({(int)response.StatusCode}): {(detail.Length <= 200 ? detail : detail[..200] + "…")}");
        }

        return response;
    }
}
