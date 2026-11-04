namespace ProtoFast.Api.Services.OffTheLoop;

/// <param name="Provider">The OpenTelemetry <c>gen_ai.provider.name</c>.</param>
public sealed record ChatRequest(string Provider, Uri Endpoint, string Model, int MaxOutputTokens, string System, string User);
