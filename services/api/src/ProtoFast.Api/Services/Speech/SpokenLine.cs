namespace ProtoFast.Api.Services.Speech;

/// <param name="RequestId">The provider's id for the call, for its span.</param>
public sealed record SpokenLine(byte[] Audio, string ContentType, long CostUsdMicros, string? RequestId = null);
