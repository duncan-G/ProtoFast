namespace ProtoFast.Api.Services.Speech;

/// <summary>A voice to cast. <paramref name="Traits"/> reads like "male · middle aged · british".</summary>
public sealed record SpeechVoice(string Id, string Name, string Description = "", string Traits = "", string PreviewUrl = "");
