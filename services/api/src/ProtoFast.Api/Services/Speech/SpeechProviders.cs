namespace ProtoFast.Api.Services.Speech;

public static class SpeechProviders
{
    /// <summary>ElevenLabs text to speech: production.</summary>
    public const string ElevenLabs = "elevenlabs";

    /// <summary>Any server speaking the OpenAI <c>/v1/audio/speech</c> API: Qwen3-TTS on vLLM-Omni in development.</summary>
    public const string OpenAi = "openai";
}
