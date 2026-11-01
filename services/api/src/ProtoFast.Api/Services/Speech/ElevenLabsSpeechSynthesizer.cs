using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace ProtoFast.Api.Services.Speech;

/// <summary>
/// ElevenLabs text to speech. Speakers aren't cast, so each is given one of the account's voices by
/// a hash of their id; the list is read once per process.
/// </summary>
public sealed class ElevenLabsSpeechSynthesizer(ElevenLabsSpeechOptions options, HttpClient http) : ISpeechSynthesizer
{
    public const string HttpClientName = "speech-elevenlabs";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (SpeechVoice Narrator, IReadOnlyList<SpeechVoice> Cast)? _voices;

    public async Task<SpeechVoice> VoiceForAsync(Guid? speakerId, CancellationToken ct)
    {
        var (narrator, cast) = await VoicesAsync(ct);
        if (speakerId is not { } id || cast.Count == 0)
        {
            return narrator;
        }

        var hash = BitConverter.ToUInt32(SHA256.HashData(id.ToByteArray()));
        return cast[(int)(hash % (uint)cast.Count)];
    }

    public async Task<SpokenLine> SpeakAsync(string text, SpeechVoice voice, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{options.BaseUrl.TrimEnd('/')}/v1/text-to-speech/{Uri.EscapeDataString(voice.Id)}?output_format={Uri.EscapeDataString(options.OutputFormat)}")
        {
            Content = JsonContent.Create(new { text, model_id = options.Model }),
        };
        using var response = await SendAsync(message, ct);
        var audio = await response.Content.ReadAsByteArrayAsync(ct);
        var cost = (long)(text.Length / 1000m * options.PricePerThousandCharacters * 1_000_000m);
        return new SpokenLine(
            audio,
            response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg",
            cost,
            response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null);
    }

    private async Task<(SpeechVoice Narrator, IReadOnlyList<SpeechVoice> Cast)> VoicesAsync(CancellationToken ct)
    {
        if (_voices is { } known)
        {
            return known;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_voices is { } read)
            {
                return read;
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, $"{options.BaseUrl.TrimEnd('/')}/v1/voices");
            using var response = await SendAsync(message, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var voices = (json.RootElement.TryGetProperty("voices", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray()
                        .Select(v => (
                            Voice: new SpeechVoice(Str(v, "voice_id"), Str(v, "name")),
                            UseCase: v.TryGetProperty("labels", out var labels) ? Str(labels, "use_case") : ""))
                        .Where(v => v.Voice.Id.Length > 0)
                        .OrderBy(v => v.Voice.Id, StringComparer.Ordinal)
                        .ToList()
                    : []);
            if (voices.Count == 0)
            {
                throw new SpeechException("The ElevenLabs account has no voices.");
            }

            var narration = voices.FindIndex(v => v.UseCase.Contains("narrat", StringComparison.OrdinalIgnoreCase));
            var narrator = options.NarratorVoiceId.Trim() is { Length: > 0 } configured
                ? voices.Select(v => v.Voice).FirstOrDefault(v => v.Id == configured) ?? new SpeechVoice(configured, "Narrator")
                : voices[Math.Max(narration, 0)].Voice;
            _voices = (narrator, voices.Select(v => v.Voice).Where(v => v.Id != narrator.Id).ToList());
            return _voices.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        message.Headers.Add("xi-api-key", options.ApiKey is { Length: > 0 } key
            ? key
            : throw new SpeechException("No ElevenLabs API key is configured (Providers:ElevenLabs:ApiKey)."));
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

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
