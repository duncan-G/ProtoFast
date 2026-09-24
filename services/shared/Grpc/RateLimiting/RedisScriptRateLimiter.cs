using System.Diagnostics;
using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace ProtoFast.Grpc.RateLimiting;

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

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        TagAttempt();
        var result = await database.ScriptEvaluateAsync(script, ScriptParameters).ConfigureAwait(false);
        return Interpret(result);
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        TagAttempt();
        return Interpret(database.ScriptEvaluate(script, ScriptParameters));
    }

    protected override void Dispose(bool disposing)
    {
        // Nothing to release: the Redis connection is owned by the container.
    }

    private object ScriptParameters => new
    {
        key = (RedisKey)key,
        window = windowSeconds,
        max_requests = maxRequests
    };

    private void TagAttempt()
    {
        var activity = Activity.Current;
        activity?.SetTag("rate.limit.key", key);
        activity?.SetTag("rate.limit.window_seconds", windowSeconds);
        activity?.SetTag("rate.limit.max_requests", maxRequests);
        activity?.SetTag("rate.limit.algorithm", algorithm);
    }

    private static SimpleLease Interpret(RedisResult raw)
    {
        // Both scripts return {blocked, retry_after_seconds}.
        var result = (RedisValue[]?)raw
            ?? throw new InvalidOperationException("The rate-limit script returned no result.");

        var allowed = (int)result[0] == 0;
        var retryAfter = (int)result[1];

        var activity = Activity.Current;
        activity?.SetTag("rate.limit.allowed", allowed);
        activity?.SetTag("rate.limit.retry_after", retryAfter);
        if (!allowed)
        {
            activity?.AddEvent(new ActivityEvent("rate_limit.blocked"));
        }

        return new SimpleLease(allowed, retryAfter);
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
