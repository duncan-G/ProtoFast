namespace ProtoFast.Api.Services.Speech;

/// <summary>Bound from <c>Speech</c>; the ElevenLabs key comes from <c>Providers:ElevenLabs:ApiKey</c>.</summary>
public sealed class SpeechOptions
{
    public ElevenLabsSpeechOptions ElevenLabs { get; set; } = new();
}
