using System.Security.Cryptography;
using System.Text;

namespace ProtoFast.Api.Services.Speech;

public static class SpeechRequests
{
    /// <summary>Everything the audio depends on; the media cache key, and what makes a cue's audio stale.</summary>
    public static string Hash(ISpeechSynthesizer synthesizer, SpeechRequest request) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', synthesizer.Provider, synthesizer.Model, request.Voice, request.Text, request.Instructions ?? ""))));

    public static string Extension(string contentType) => contentType.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "audio/mpeg" or "audio/mp3" => "mp3",
        "audio/wav" or "audio/x-wav" or "audio/wave" => "wav",
        "audio/flac" or "audio/x-flac" => "flac",
        "audio/ogg" or "audio/opus" => "ogg",
        "audio/aac" => "aac",
        _ => "bin",
    };
}
