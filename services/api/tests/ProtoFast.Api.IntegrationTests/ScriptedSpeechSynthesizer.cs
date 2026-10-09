using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Speech;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Speaks every line as the bytes of its own text, and remembers what it was asked.</summary>
public sealed class ScriptedSpeechSynthesizer : ISpeechSynthesizer
{
    public List<SpeechRequest> Spoken { get; } = [];

    public string Provider => "scripted";

    public string Model => "scripted-1";

    public SpeechRequest Prepare(string performance, string? voiceId) =>
        new(Performances.Strip(performance), string.Join(",", Performances.Tags(performance)) is { Length: > 0 } t ? t : null, voiceId ?? "default");

    public Task<SpokenLine> SpeakAsync(SpeechRequest request, CancellationToken ct)
    {
        lock (Spoken)
        {
            Spoken.Add(request);
        }

        return Task.FromResult(new SpokenLine(System.Text.Encoding.UTF8.GetBytes($"{request.Voice}:{request.Text}"), "audio/mpeg", 7));
    }

    public Task<IReadOnlyList<SpeechVoice>> ListVoicesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SpeechVoice>>([new SpeechVoice("v1", "Vivian"), new SpeechVoice("v2", "Ryan")]);
}
