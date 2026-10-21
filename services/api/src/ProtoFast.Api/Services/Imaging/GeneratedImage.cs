namespace ProtoFast.Api.Services.Imaging;

/// <param name="Notes">The model's summary of its reasoning, if it gave one.</param>
/// <param name="Usage">What the provider reported about the call, for its span.</param>
public sealed record GeneratedImage(byte[] Bytes, string ContentType, long CostUsdMicros, string? Notes, ImageUsage? Usage = null);
