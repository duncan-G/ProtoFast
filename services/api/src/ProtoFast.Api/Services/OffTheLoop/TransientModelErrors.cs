using System.Net;
using System.Net.Sockets;
using Anthropic.Exceptions;
using Anthropic.Models;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Which model failures are the provider's and pass: outages, rate limits, timeouts and broken connections.</summary>
internal static class TransientModelErrors
{
    /// <param name="ct">The caller's token: a cancellation it did not ask for is a timeout.</param>
    public static bool IsTransient(Exception e, CancellationToken ct) => e switch
    {
        OperationCanceledException => !ct.IsCancellationRequested,
        AnthropicRateLimitException or Anthropic5xxException or AnthropicIOException => true,
        AnthropicApiException api => IsTransient(api.StatusCode),
        AnthropicSseException sse => sse.ErrorType
            is null or ErrorType.RateLimitError or ErrorType.TimeoutError or ErrorType.OverloadedError or ErrorType.ApiError,
        HttpRequestException http => http.StatusCode is not { } status || IsTransient(status),
        IOException or SocketException or TimeoutException => true,
        _ => false,
    };

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests
        || (int)status >= 500;
}
