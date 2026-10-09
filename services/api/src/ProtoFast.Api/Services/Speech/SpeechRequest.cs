namespace ProtoFast.Api.Services.Speech;

/// <summary>What a provider is asked to say: the words as it should receive them, style apart, in one voice.</summary>
public sealed record SpeechRequest(string Text, string? Instructions, string Voice);
