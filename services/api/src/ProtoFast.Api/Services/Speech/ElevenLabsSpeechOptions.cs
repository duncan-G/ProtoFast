namespace ProtoFast.Api.Services.Speech;

/// <summary>Bound from <c>Speech:ElevenLabs</c>; the key comes from <c>Providers:ElevenLabs:ApiKey</c>.</summary>
public sealed class ElevenLabsSpeechOptions
{
    public string BaseUrl { get; set; } = "https://api.elevenlabs.io";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "eleven_v4";

    /// <summary>Empty takes the account's first voice made for narration.</summary>
    public string NarratorVoiceId { get; set; } = "";

    public string OutputFormat { get; set; } = "mp3_44100_128";

    /// <summary>USD per thousand characters, for the cost shown.</summary>
    public decimal PricePerThousandCharacters { get; set; } = 0.30m;
}
