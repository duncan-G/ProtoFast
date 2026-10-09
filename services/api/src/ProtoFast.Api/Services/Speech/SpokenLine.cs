namespace ProtoFast.Api.Services.Speech;

public sealed record SpokenLine(byte[] Audio, string ContentType, long CostUsdMicros);
