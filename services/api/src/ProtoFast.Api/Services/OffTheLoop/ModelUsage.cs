namespace ProtoFast.Api.Services.OffTheLoop;

/// <param name="InputTokens">The whole prompt, its cached part included.</param>
public sealed record ModelUsage(long InputTokens, long CachedInputTokens, long OutputTokens);
