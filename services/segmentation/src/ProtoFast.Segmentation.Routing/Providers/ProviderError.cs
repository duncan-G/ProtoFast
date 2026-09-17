using System.Net;
using Microsoft.Extensions.AI;

namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>Normalized failure kinds across four providers (plan §14.3).</summary>
public enum ProviderErrorKind
{
    RateLimited,
    Overloaded,
    Timeout,
    BadRequest,
    ContextTooLong,
    Auth,
    ContentPolicy,
    Unknown,
}

/// <summary>
/// A provider failure, classified. The classification is the whole reason this type exists: the
/// retry policy, the circuit breaker and the window re-planner all branch on
/// <see cref="Kind"/>, and four SDKs report the same conditions four different ways.
/// </summary>
public sealed class ProviderException(
    ProviderErrorKind kind, string provider, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ProviderErrorKind Kind { get; } = kind;

    public string Provider { get; } = provider;

    /// <summary>The provider's own <c>retry-after</c>, honoured ahead of the backoff schedule.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Retryable kinds are the transient ones. <see cref="ProviderErrorKind.ContextTooLong"/> is
    /// deliberately absent: retrying the same oversized window would fail identically, so it
    /// triggers a re-plan instead (plan §14.7).
    /// </summary>
    public bool IsRetryable => Kind is ProviderErrorKind.RateLimited or ProviderErrorKind.Overloaded or ProviderErrorKind.Timeout;

    /// <summary>
    /// Classifies whatever the SDK threw. Status code first, because it is the one signal every
    /// provider agrees on; message matching is the fallback for the cases HTTP cannot express —
    /// a 400 that means "your context is too long" and a 400 that means "your JSON is malformed"
    /// need different responses.
    /// </summary>
    public static ProviderException From(Exception exception, string provider)
    {
        if (exception is ProviderException already)
        {
            return already;
        }

        if (exception is OperationCanceledException or TimeoutException)
        {
            return new ProviderException(ProviderErrorKind.Timeout, provider, "The provider call timed out.", exception);
        }

        var status = StatusOf(exception);
        var message = exception.Message;

        var kind = status switch
        {
            HttpStatusCode.TooManyRequests => ProviderErrorKind.RateLimited,
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout
                => ProviderErrorKind.Overloaded,
            HttpStatusCode.RequestTimeout => ProviderErrorKind.Timeout,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderErrorKind.Auth,
            HttpStatusCode.RequestEntityTooLarge => ProviderErrorKind.ContextTooLong,
            HttpStatusCode.BadRequest when MentionsContextLength(message) => ProviderErrorKind.ContextTooLong,
            HttpStatusCode.BadRequest when MentionsContentPolicy(message) => ProviderErrorKind.ContentPolicy,
            HttpStatusCode.BadRequest => ProviderErrorKind.BadRequest,
            _ when MentionsContextLength(message) => ProviderErrorKind.ContextTooLong,
            _ when MentionsContentPolicy(message) => ProviderErrorKind.ContentPolicy,
            // Anthropic reports capacity pressure as a 529, which HttpStatusCode has no name for.
            _ when (int?)status == 529 => ProviderErrorKind.Overloaded,
            _ => ProviderErrorKind.Unknown,
        };

        return new ProviderException(kind, provider, message, exception)
        {
            RetryAfter = RetryAfterOf(exception),
        };
    }

    private static bool MentionsContextLength(string message) =>
        message.Contains("context length", StringComparison.OrdinalIgnoreCase)
        || message.Contains("context_length", StringComparison.OrdinalIgnoreCase)
        || message.Contains("maximum context", StringComparison.OrdinalIgnoreCase)
        || message.Contains("too many tokens", StringComparison.OrdinalIgnoreCase)
        || message.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsContentPolicy(string message) =>
        message.Contains("content policy", StringComparison.OrdinalIgnoreCase)
        || message.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
        || message.Contains("safety", StringComparison.OrdinalIgnoreCase);

    private static HttpStatusCode? StatusOf(Exception exception) => exception switch
    {
        HttpRequestException http => http.StatusCode,
        _ when exception.Data["StatusCode"] is int code => (HttpStatusCode)code,
        { InnerException: { } inner } => StatusOf(inner),
        _ => null,
    };

    private static TimeSpan? RetryAfterOf(Exception exception) =>
        exception.Data["RetryAfter"] switch
        {
            TimeSpan span => span,
            int seconds => TimeSpan.FromSeconds(seconds),
            string text when double.TryParse(text, out var seconds) => TimeSpan.FromSeconds(seconds),
            _ => exception.InnerException is { } inner ? RetryAfterOf(inner) : null,
        };
}

/// <summary>Rate-limit facts scraped from a provider's response headers (plan §14.3).</summary>
public sealed record RateLimitSnapshot(int? RemainingRequests, long? RemainingTokens, TimeSpan? ResetAfter)
{
    public static readonly RateLimitSnapshot Empty = new(null, null, null);

    public bool HasAnything => RemainingRequests is not null || RemainingTokens is not null;
}

/// <summary>Usage a call reported, normalized across SDKs.</summary>
public sealed record CallUsage(int InputTokens, int OutputTokens, int CachedInputTokens)
{
    public static readonly CallUsage Empty = new(0, 0, 0);

    public static CallUsage From(UsageDetails? usage) => usage is null
        ? Empty
        : new CallUsage(
            (int)(usage.InputTokenCount ?? 0),
            (int)(usage.OutputTokenCount ?? 0),
            (int)(usage.AdditionalCounts?.GetValueOrDefault("InputTokenCount.CacheRead") ?? 0));
}
