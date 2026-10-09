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

    public List<VoiceQuery> Searches { get; } = [];

    /// <summary>Narration finds Ada alone; casting filters the library on its traits.</summary>
    public Task<IReadOnlyList<SpeechVoice>> SearchVoicesAsync(VoiceQuery query, CancellationToken ct)
    {
        Searches.Add(query);
        SpeechVoice[] library =
        [
            new("n1", "Ada", "Warm and unhurried.", "female · middle aged · british", "https://example.test/ada.mp3"),
            new("c1", "Isla", "Bright and quick.", "female · middle aged · scottish", "https://example.test/isla.mp3"),
            new("c2", "Fiona", "Light.", "female · young · scottish", ""),
            new("c3", "Rob", "Gravel.", "male · old · american", ""),
            new("c4", "Morag", "Low and dry.", "female · middle aged · scottish", "https://example.test/morag.mp3"),
        ];
        return Task.FromResult<IReadOnlyList<SpeechVoice>>(query.Narration
            ? [library[0]]
            : library.Where(v => new[] { query.Gender, query.Age.Replace('_', ' '), query.Accent }
                    .All(label => label.Length == 0 || v.Traits.Split(" · ").Contains(label)))
                .ToList());
    }
}
