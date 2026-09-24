using System.Diagnostics;
using System.Threading.RateLimiting;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Exceptions;
using StackExchange.Redis;

namespace ProtoFast.Api.RateLimiting;

public static class RateLimiterHttpContextExtensions
{
    private static readonly ActivitySource ActivitySource = new("ProtoFast.Api.RateLimiting");

    extension(ServerCallContext context)
    {
        public async Task EnforceFixedLimitByIdentityAsync(
            int windowSeconds,
            int maxRequests,
            CancellationToken cancellationToken = default)
        {
            var limiter = context.CreateFixedLimiterByIdentity(windowSeconds, maxRequests);
            await context.EnforceAsync(limiter, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task EnforceSlidingLimitByIdentityAsync(
            int windowSeconds,
            int maxRequests,
            CancellationToken cancellationToken = default)
        {
            var limiter = context.CreateSlidingLimiterByIdentity(windowSeconds, maxRequests);
            await context.EnforceAsync(limiter, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task EnforceFixedLimitByIpAsync(
            int windowSeconds,
            int maxRequests,
            CancellationToken cancellationToken = default)
        {
            var limiter = context.CreateFixedLimiterByIp(windowSeconds, maxRequests);
            await context.EnforceAsync(limiter, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task EnforceSlidingLimitByIpAsync(
            int windowSeconds,
            int maxRequests,
            CancellationToken cancellationToken = default)
        {
            var limiter = context.CreateSlidingLimiterByIp(windowSeconds, maxRequests);
            await context.EnforceAsync(limiter, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnforceAsync(
        this ServerCallContext context,
        RateLimiter limiter,
        CancellationToken cancellationToken = default)
    {
        var httpContext = context.GetHttpContext();
        var caller = CallerIdentity.From(context);

        using var activity = ActivitySource.StartActivity("rate_limit.enforce");
        activity?.SetTag("http.request.method", httpContext.Request.Method);
        activity?.SetTag("http.route", httpContext.Request.Path.ToString());
        activity?.SetTag("enduser.id",  caller.Subject);

        var ip = httpContext.GetRemoteIp();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            activity?.SetTag("client.address", ip);
        }

        using var lease = await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            // Extract retry-after information from the lease
            var retryAfterSeconds = 0;
            if (lease.TryGetMetadata("RetryAfter", out var retryAfterObj) && retryAfterObj is int retryAfter)
            {
                retryAfterSeconds = retryAfter;
                activity?.SetTag("rate.limit.retry_after", retryAfterSeconds);
            }

            // Create error message with retry-after information
            var message = retryAfterSeconds > 0
                ? $"Rate limit exceeded. Try again in {Math.Ceiling(retryAfterSeconds / 60.0)} minutes."
                : "Rate limit exceeded.";

            var descriptor = new GrpcErrorDescriptor(
                StatusCode.ResourceExhausted,
                ErrorCodes.ResourceExhausted,
                Message: message);

            var exception = descriptor.ToRpcException();

            // Add retry-after as gRPC metadata
            if (retryAfterSeconds > 0)
            {
                exception.Trailers.Add("retry-after-seconds", retryAfterSeconds.ToString());
            }

            throw exception;
        }
    }

    extension(ServerCallContext context)
    {

        private RedisScriptRateLimiter CreateFixedLimiterByIdentity(int windowSeconds, int maxRequests)
        {
            var caller = CallerIdentity.From(context);
            var subject = caller.Subject;
            var httpContext = context.GetHttpContext();
            var db = httpContext.GetRedisDb();
            var path = httpContext.GetPath();
            var key = string.IsNullOrEmpty(path)
                ? $"fixed:user:{subject}"
                : $"fixed:{path}:user:{subject}";

            return new RedisScriptRateLimiter(db, Scripts.FixedWindowScript, key, windowSeconds, maxRequests, "fixed");
        }

        private RedisScriptRateLimiter CreateSlidingLimiterByIdentity(int windowSeconds, int maxRequests)
        {
            var caller = CallerIdentity.From(context);
            var subject = caller.Subject;
            var httpContext = context.GetHttpContext();
            var db = httpContext.GetRedisDb();
            var path = httpContext.GetPath();
            var key = string.IsNullOrEmpty(path)
                ? $"sliding:user:{subject}"
                : $"sliding:{path}:user:{subject}";

            return new RedisScriptRateLimiter(db, Scripts.SlidingWindowScript, key, windowSeconds, maxRequests, "sliding");
        }

        private RedisScriptRateLimiter CreateFixedLimiterByIp(int windowSeconds, int maxRequests)
        {
            var httpContext = context.GetHttpContext();
            var db = httpContext.GetRedisDb();
            var path = httpContext.GetPath();
            var ip = httpContext.GetRemoteIp() ?? "unknown";
            var key = string.IsNullOrEmpty(path)
                ? $"fixed:ip:{ip}"
                : $"fixed:{path}:ip:{ip}";

            return new RedisScriptRateLimiter(db, Scripts.FixedWindowScript, key, windowSeconds, maxRequests, "fixed");
        }

        private RedisScriptRateLimiter CreateSlidingLimiterByIp(int windowSeconds, int maxRequests)
        {
            var httpContext = context.GetHttpContext();
            var db = httpContext.GetRedisDb();
            var path = httpContext.GetPath();
            var ip = httpContext.GetRemoteIp() ?? "unknown";
            var key = string.IsNullOrEmpty(path)
                ? $"sliding:ip:{ip}"
                : $"sliding:{path}:ip:{ip}";

            return new RedisScriptRateLimiter(db, Scripts.SlidingWindowScript, key, windowSeconds, maxRequests, "sliding");
        }
    }

    extension(HttpContext context)
    {
        private string? GetRemoteIp()
            => context.Connection.RemoteIpAddress?.ToString();

        private IDatabase GetRedisDb()
        {
            var sp = context.RequestServices;
            var mux = sp.GetRequiredService<IConnectionMultiplexer>();
            return mux.GetDatabase();
        }

        private string GetPath()
            => context.Request.Path.ToString();
    }
}
