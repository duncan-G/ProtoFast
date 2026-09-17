using System.Globalization;

namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>
/// Captures rate-limit headers off the wire (plan §14.3).
///
/// <para><c>IChatClient</c> hands back a response object, not an HTTP response, so the headers
/// that say how much quota is left never reach the caller. A <see cref="DelegatingHandler"/> on
/// the underlying <see cref="HttpClient"/> is the only place they are visible.</para>
///
/// <para>Getting the snapshot back to the right caller is the awkward part: dozens of calls are
/// in flight across four providers, so a field on the handler would be a last-write-wins race.
/// The caller opens a <see cref="Capture"/> scope around its call instead; the scope lives in an
/// <see cref="AsyncLocal{T}"/>, so it flows <em>down</em> into whatever continuation the SDK
/// runs the request on and nowhere else. The handler writes into the scope it finds there, and a
/// call with no scope open simply has its headers dropped.</para>
/// </summary>
public sealed class RateLimitHeaderHandler : DelegatingHandler
{
    private static readonly AsyncLocal<CaptureScope?> Current = new();

    /// <summary>
    /// Opens a capture scope for the current async flow. Read <see cref="CaptureScope.Snapshot"/>
    /// after the call; dispose closes the scope so a later unrelated call cannot write into it.
    /// </summary>
    public static CaptureScope Capture()
    {
        var scope = new CaptureScope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    public sealed class CaptureScope(CaptureScope? previous) : IDisposable
    {
        /// <summary>What the provider last reported, or <see cref="RateLimitSnapshot.Empty"/>.</summary>
        public RateLimitSnapshot Snapshot { get; internal set; } = RateLimitSnapshot.Empty;

        public void Dispose() => Current.Value = previous;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (Current.Value is { } scope)
        {
            var snapshot = Read(response);
            if (snapshot.HasAnything)
            {
                scope.Snapshot = snapshot;
            }
        }

        // Retry-after and the status code have to reach the classifier, and the SDK's own
        // exception may not carry either.
        if (!response.IsSuccessStatusCode)
        {
            AttachDiagnostics(response);
        }

        return response;
    }

    private static RateLimitSnapshot Read(HttpResponseMessage response)
    {
        // Anthropic's names are the most specific; the OpenAI-compatible providers (DeepSeek,
        // Kimi) use the x-ratelimit-remaining-* family. Reading both is cheaper than maintaining
        // a per-provider handler, and a provider that sends neither falls through to adaptive
        // concurrency, which is the documented fallback anyway.
        var remainingRequests =
            ReadInt(response, "anthropic-ratelimit-requests-remaining")
            ?? ReadInt(response, "x-ratelimit-remaining-requests");

        var remainingTokens =
            ReadLong(response, "anthropic-ratelimit-input-tokens-remaining")
            ?? ReadLong(response, "anthropic-ratelimit-tokens-remaining")
            ?? ReadLong(response, "x-ratelimit-remaining-tokens");

        var reset =
            ReadReset(response, "anthropic-ratelimit-requests-reset")
            ?? ReadReset(response, "x-ratelimit-reset-requests")
            ?? ReadRetryAfter(response);

        return new RateLimitSnapshot(remainingRequests, remainingTokens, reset);
    }

    private static void AttachDiagnostics(HttpResponseMessage response)
    {
        response.RequestMessage?.Options.Set(
            new HttpRequestOptionsKey<int>("pf-status"), (int)response.StatusCode);
    }

    private static int? ReadInt(HttpResponseMessage response, string header) =>
        First(response, header) is { } value && int.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static long? ReadLong(HttpResponseMessage response, string header) =>
        First(response, header) is { } value && long.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Reset headers come as either an absolute RFC-3339 timestamp (Anthropic) or a duration
    /// like <c>6m0s</c> / <c>1.5s</c> (OpenAI-compatible). Both are handled; anything else is
    /// treated as absent rather than guessed at.
    /// </summary>
    private static TimeSpan? ReadReset(HttpResponseMessage response, string header)
    {
        var value = First(response, header);
        if (value is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out var absolute))
        {
            var delta = absolute - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return ParseDuration(value);
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta
        ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

    internal static TimeSpan? ParseDuration(string value)
    {
        var total = TimeSpan.Zero;
        var number = 0d;
        var digits = string.Empty;

        foreach (var c in value)
        {
            if (char.IsDigit(c) || c == '.')
            {
                digits += c;
                continue;
            }

            if (!double.TryParse(digits, CultureInfo.InvariantCulture, out number))
            {
                return null;
            }

            total += c switch
            {
                'h' => TimeSpan.FromHours(number),
                'm' when digits.Length > 0 => TimeSpan.FromMinutes(number),
                's' => TimeSpan.FromSeconds(number),
                _ => TimeSpan.Zero,
            };

            digits = string.Empty;
        }

        // A bare number with no unit is seconds, which is how retry-after is usually sent.
        if (digits.Length > 0 && double.TryParse(digits, CultureInfo.InvariantCulture, out number))
        {
            total += TimeSpan.FromSeconds(number);
        }

        return total > TimeSpan.Zero ? total : null;
    }

    private static string? First(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? values.FirstOrDefault() : null;
}
