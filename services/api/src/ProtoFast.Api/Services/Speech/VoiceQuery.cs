namespace ProtoFast.Api.Services.Speech;

/// <summary>
/// Filters on the provider's voice library; empty means any. Language is ISO 639-1, and
/// <paramref name="Narration"/> keeps to voices made for narration.
/// </summary>
public sealed record VoiceQuery(
    string Language,
    string Search = "",
    string Gender = "",
    string Age = "",
    string Accent = "",
    bool Narration = false,
    int Limit = 100);
