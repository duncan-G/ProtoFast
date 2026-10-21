using System.Diagnostics;

namespace ProtoFast.Api.Services.Speech;

public static class SpeechTelemetry
{
    /// <summary>Under <c>ProtoFast.*</c>, so the service defaults export it.</summary>
    public const string SourceName = "ProtoFast.Api.Speech";

    public static readonly ActivitySource Source = new(SourceName);
}
