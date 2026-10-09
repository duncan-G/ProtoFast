namespace ProtoFast.Api.Services.Imaging;

/// <param name="CachedInputTokens">Input tokens served from the provider's prompt cache.</param>
public sealed record ImageUsage(
    string? ResponseId,
    string? ResponseModel,
    string? Status,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens);
