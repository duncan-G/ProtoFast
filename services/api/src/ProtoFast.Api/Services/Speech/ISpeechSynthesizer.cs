namespace ProtoFast.Api.Services.Speech;

public interface ISpeechSynthesizer
{
    /// <summary>One of <see cref="SpeechProviders"/>.</summary>
    string Provider { get; }

    string Model { get; }

    /// <summary>
    /// Turns a cue's performance into what this provider is asked: tags go where it understands
    /// them (in the text, or as instructions) and a missing voice takes the default.
    /// </summary>
    SpeechRequest Prepare(string performance, string? voiceId);

    Task<SpokenLine> SpeakAsync(SpeechRequest request, CancellationToken ct);

    Task<IReadOnlyList<SpeechVoice>> ListVoicesAsync(CancellationToken ct);

    /// <summary>The provider's voice library, most used first.</summary>
    Task<IReadOnlyList<SpeechVoice>> SearchVoicesAsync(VoiceQuery query, CancellationToken ct);
}
