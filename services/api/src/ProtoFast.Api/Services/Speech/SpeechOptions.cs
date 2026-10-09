namespace ProtoFast.Api.Services.Speech;

/// <summary>Bound from <c>Speech</c>; the API keys arrive from Secrets Manager under the same path.</summary>
public sealed class SpeechOptions
{
    /// <summary>One of <see cref="SpeechProviders"/>.</summary>
    public string Provider { get; set; } = SpeechProviders.ElevenLabs;

    public OpenAiSpeechOptions OpenAi { get; set; } = new();

    public ElevenLabsSpeechOptions ElevenLabs { get; set; } = new();
}
