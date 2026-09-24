using System.Diagnostics;
using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace ProtoFast.Api.RateLimiting;

internal sealed class RedisScriptRateLimiter(
    IDatabase database,
    LuaScript script,
    string key,
    int windowSeconds,
    int maxRequests,
    string algorithm)
    : RateLimiter
{
    public override TimeSpan? IdleDuration => null;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var (allowed, retryAfter) = await EvaluateScriptAsync(cancellationToken).ConfigureAwait(false);
        return new SimpleLease(allowed, retryAfter);
    }

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var (allowed, retryAfter) = EvaluateScriptSync();
        return new SimpleLease(allowed, retryAfter);
    }

    protected override void Dispose(bool disposing)
    {
        // no-op
    }

    private (bool allowed, int retryAfter) EvaluateScriptSync()
    {
        var activity = Activity.Current;
        activity?.SetTag("rate.limit.key", key);
        activity?.SetTag("rate.limit.window_seconds", windowSeconds);
        activity?.SetTag("rate.limit.max_requests", maxRequests);
        activity?.SetTag("rate.limit.algorithm", algorithm);

        var result = (RedisValue[])database.ScriptEvaluate(script, new
        {
            key = (RedisKey)key,
            window = windowSeconds,
            max_requests = maxRequests
        });

        var allowed = (int)result[0] == 0;
        var retryAfter = (int)result[1];

        activity?.SetTag("rate.limit.allowed", allowed);
        activity?.SetTag("rate.limit.retry_after", retryAfter);
        if (!allowed)
        {
            activity?.AddEvent(new ActivityEvent("rate_limit.blocked"));
        }
        return (allowed, retryAfter);
    }

    private async Task<(bool allowed, int retryAfter)> EvaluateScriptAsync(CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        activity?.SetTag("rate.limit.key", key);
        activity?.SetTag("rate.limit.window_seconds", windowSeconds);
        activity?.SetTag("rate.limit.max_requests", maxRequests);
        activity?.SetTag("rate.limit.algorithm", algorithm);

        var task = database.ScriptEvaluateAsync(script, new
        {
            key = (RedisKey)key,
            window = windowSeconds,
            max_requests = maxRequests
        });
        var result = (RedisValue[])await task.ConfigureAwait(false);

        var allowed = (int)result[0] == 0;
        var retryAfter = (int)result[1];

        activity?.SetTag("rate.limit.allowed", allowed);
        activity?.SetTag("rate.limit.retry_after", retryAfter);
        if (!allowed)
        {
            activity?.AddEvent(new ActivityEvent("rate_limit.blocked"));
        }
        return (allowed, retryAfter);
    }

    private sealed class SimpleLease(bool isAcquired, int retryAfterSeconds = 0) : RateLimitLease
    {
        public override bool IsAcquired => isAcquired;

        public override IEnumerable<string> MetadataNames => ["RetryAfter"];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == "RetryAfter")
            {
                metadata = retryAfterSeconds;
                return true;
            }
            metadata = null;
            return false;
        }
    }
}


