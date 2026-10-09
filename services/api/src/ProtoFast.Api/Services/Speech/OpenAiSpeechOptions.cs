namespace ProtoFast.Api.Services.Speech;

public sealed class OpenAiSpeechOptions
{
    /// <summary>The server's <c>/v1</c> URL.</summary>
    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice";

    /// <summary>The voice a character without one is spoken in.</summary>
    public string DefaultVoice { get; set; } = "vivian";

    public string ResponseFormat { get; set; } = "mp3";

    /// <summary>Qwen3-TTS takes the language by name; null sends none.</summary>
    public string? Language { get; set; } = "English";
}
