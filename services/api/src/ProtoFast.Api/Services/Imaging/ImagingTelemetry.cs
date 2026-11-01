using System.Diagnostics;

namespace ProtoFast.Api.Services.Imaging;

public static class ImagingTelemetry
{
    /// <summary>Under <c>ProtoFast.*</c>, so the service defaults export it.</summary>
    public const string SourceName = "ProtoFast.Api.Imaging";

    public static readonly ActivitySource Source = new(SourceName);
}
