using System.Collections.Concurrent;
using ProtoFast.Api.Services.Speech;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Speaks nothing; keeps each line with the voice it was given.</summary>
public sealed class RecordingSpeechSynthesizer : ISpeechSynthesizer
{
    public static readonly SpeechVoice Narrator = new("narrator", "Narrator");

    public ConcurrentQueue<(string Text, SpeechVoice Voice)> Lines { get; } = new();

    public Task<SpeechVoice> VoiceForAsync(Guid? speakerId, CancellationToken ct) =>
        Task.FromResult(speakerId is { } id ? new SpeechVoice($"voice-{id}", $"Voice of {id}") : Narrator);

    public Task<SpokenLine> SpeakAsync(string text, SpeechVoice voice, CancellationToken ct)
    {
        Lines.Enqueue((text, voice));
        return Task.FromResult(new SpokenLine([0xFF, 0xFB], "audio/mpeg", 300, "req_1"));
    }
}
