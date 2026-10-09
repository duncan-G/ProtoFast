namespace ProtoFast.Api.Services.Speech;

public sealed class ElevenLabsSpeechOptions
{
    public string BaseUrl { get; set; } = "https://api.elevenlabs.io";

    public string? ApiKey { get; set; }

    /// <summary>v3 and v4 perform the delivery tags; older models would read them out, so they are stripped there.</summary>
    public string Model { get; set; } = "eleven_v4";

    /// <summary>The voice library's use case narrators are offered from.</summary>
    public string NarratorUseCase { get; set; } = "narrative_story";

    /// <summary>The voice a character without one is spoken in; empty means every speaker must be cast.</summary>
    public string DefaultVoiceId { get; set; } = "";

    public string OutputFormat { get; set; } = "mp3_44100_128";

    /// <summary>USD per thousand characters, for the cost recorded on each asset.</summary>
    public decimal PricePerThousandCharacters { get; set; } = 0.30m;
}
