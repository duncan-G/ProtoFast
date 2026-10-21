namespace ProtoFast.Api.Services.Speech;

/// <summary>The speech provider could not be used; the message is worded for the operator.</summary>
public sealed class SpeechException(string message, Exception? inner = null) : Exception(message, inner);
