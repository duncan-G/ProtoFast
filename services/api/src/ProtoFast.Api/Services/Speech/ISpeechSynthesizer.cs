namespace ProtoFast.Api.Services.Speech;

public interface ISpeechSynthesizer
{
    /// <summary>The narrator's voice for null; otherwise the same voice for a speaker every time.</summary>
    Task<SpeechVoice> VoiceForAsync(Guid? speakerId, CancellationToken ct);

    Task<SpokenLine> SpeakAsync(string text, SpeechVoice voice, CancellationToken ct);
}
