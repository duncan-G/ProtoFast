using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Exceptions;
using StackExchange.Redis;

namespace ProtoFast.Grpc.RateLimiting;

/// <summary>
/// Per-call rate limiting for gRPC services, enforced in Redis so the limit holds across
/// instances. The <c>ByIdentity</c> variants key on the internal JWT's subject and therefore
/// require an authenticated caller; the <c>ByIp</c> variants work on anonymous calls too.
/// </summary>
public static class RateLimitExtensions
{
    private static readonly ActivitySource ActivitySource = new("ProtoFast.Grpc.RateLimiting");

    extension(ServerCallContext context)
    {
        public Task EnforceFixedLimitByIdentityAsync(
            int windowSeconds,
            int maxRequests,
            CancellationToken cancellationToken = default)
        {
            var subject = CallerIdentity.From(context).Subject;
            return context.EnforceAsync(
                Scripts.FixedWindowScript, "fixed", $"user:{subject}", subject, windowSeconds, maxRequests, cancellationToken);
        }

        public Task EnforceSlidingLimitByIdentityAsync(
            int windowSeconds,
            int maxRequests,
            CancellationToken cancellationToken = default)
        {
            var subject = CallerIdentity.From(context).Subject;
            return context.EnforceAsync(
                Scripts.SlidingWindowScript, "sliding", $"user:{subject}", subject, windowSeconds, maxRequests, cancellationToken);
        }

        public Task EnforceFixedLimitByIpAsync(
            int windowSeconds,
            int maxRequests,
            CancellationToken cancellationToken = default)
        {
            var ip = context.GetHttpContext().Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return context.EnforceAsync(
                Scripts.FixedWindowScript, "fixed", $"ip:{ip}", subject: null, windowSeconds, maxRequests, cancellationToken);
        }

        public Task EnforceSlidingLimitByIpAsync(
            int windowSeconds,
            int maxRequests,
            CancellationToken cancellationToken = default)
        {
            var ip = context.GetHttpContext().Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return context.EnforceAsync(
                Scripts.SlidingWindowScript, "sliding", $"ip:{ip}", subject: null, windowSeconds, maxRequests, cancellationToken);
        }
    }

    private static async Task EnforceAsync(
        this ServerCallContext context,
        LuaScript script,
        string algorithm,
        string dimension,
        string? subject,
        int windowSeconds,
        int maxRequests,
        CancellationToken cancellationToken)
    {
        var httpContext = context.GetHttpContext();
        var path = httpContext.Request.Path.ToString();

        // Scoped to the call path so each RPC gets its own budget rather than sharing one.
        var key = string.IsNullOrEmpty(path)
            ? $"{algorithm}:{dimension}"
            : $"{algorithm}:{path}:{dimension}";

        var redis = httpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        using var limiter = new RedisScriptRateLimiter(redis, script, key, windowSeconds, maxRequests, algorithm);

        using var activity = ActivitySource.StartActivity("rate_limit.enforce");
        activity?.SetTag("http.request.method", httpContext.Request.Method);
        activity?.SetTag("http.route", path);
        if (subject is not null)
        {
            activity?.SetTag("enduser.id", subject);
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            activity?.SetTag("client.address", ip);
        }

        using var lease = await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (lease.IsAcquired)
        {
            return;
        }

        var retryAfterSeconds = 0;
        if (lease.TryGetMetadata("RetryAfter", out var retryAfterObj) && retryAfterObj is int retryAfter)
        {
            retryAfterSeconds = retryAfter;
            activity?.SetTag("rate.limit.retry_after", retryAfterSeconds);
        }

        var message = retryAfterSeconds > 0
            ? $"Rate limit exceeded. Try again in {Math.Ceiling(retryAfterSeconds / 60.0)} minutes."
            : "Rate limit exceeded.";

        var exception = new GrpcErrorDescriptor(
                StatusCode.ResourceExhausted,
                ErrorCodes.ResourceExhausted,
                Message: message)
            .ToRpcException();

        if (retryAfterSeconds > 0)
        {
            exception.Trailers.Add("retry-after-seconds", retryAfterSeconds.ToString());
        }

        throw exception;
    }
}
