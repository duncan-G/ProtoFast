using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ProtoFast.Segmentation.Routing.Budgets;

/// <summary>
/// The Redis implementation (plan §14.4). Everything lives under the <c>seg:budget:</c> prefix,
/// which is the only keyspace this feature adds to the shared Redis.
///
/// <para>Reservation is a single Lua script rather than a sequence of round trips. Two workers
/// checking headroom and then reserving is a classic check-then-act race — under load both would
/// see room and both would take it — and the failure mode is a 429 storm, which is exactly the
/// thing the ledger exists to prevent.</para>
/// </summary>
public sealed class RedisBudgetLedger(
    IConnectionMultiplexer redis,
    IModelRegistry registry,
    IOptions<RoutingOptions> options,
    ILogger<RedisBudgetLedger> logger) : IBudgetLedger
{
    private const string Prefix = "seg:budget:";

    /// <summary>
    /// Reserve across every dimension atomically.
    ///
    /// <para>Each dimension is a sliding-window counter held in a sorted set keyed by pool and
    /// dimension: members are reservation tokens scored by their timestamp, so expiry is a range
    /// trim rather than a background sweep. The script trims first, sums what is left, and only
    /// then writes — so a reservation is either fully granted or not made at all.</para>
    ///
    /// <para>KEYS: rpm, itpm, otpm, concurrency, daily-tokens, daily-usd.
    /// ARGV: now(ms), window(ms), token, input, output, cost, limits…, ttl(ms).</para>
    /// </summary>
    private const string ReserveScript = """
        local now        = tonumber(ARGV[1])
        local window     = tonumber(ARGV[2])
        local token      = ARGV[3]
        local input      = tonumber(ARGV[4])
        local output     = tonumber(ARGV[5])
        local cost       = tonumber(ARGV[6])
        local rpmLimit   = tonumber(ARGV[7])
        local itpmLimit  = tonumber(ARGV[8])
        local otpmLimit  = tonumber(ARGV[9])
        local concLimit  = tonumber(ARGV[10])
        local dayTokens  = tonumber(ARGV[11])
        local dayUsd     = tonumber(ARGV[12])
        local ttl        = tonumber(ARGV[13])

        local cutoff = now - window

        -- Sum a sliding window after trimming what has aged out of it.
        local function windowed(key)
          redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoff)
          local total = 0
          local entries = redis.call('ZRANGE', key, 0, -1, 'WITHSCORES')
          for i = 1, #entries, 2 do
            local _, _, amount = string.find(entries[i], '|(%-?%d+%.?%d*)$')
            total = total + (tonumber(amount) or 0)
          end
          return total
        end

        local used_rpm  = windowed(KEYS[1])
        local used_itpm = windowed(KEYS[2])
        local used_otpm = windowed(KEYS[3])

        -- Concurrency is in flight rather than per minute, so its window is the reservation TTL.
        redis.call('ZREMRANGEBYSCORE', KEYS[4], '-inf', now - ttl)
        local inflight = redis.call('ZCARD', KEYS[4])

        if rpmLimit  > 0 and used_rpm  + 1      > rpmLimit  then return 0 end
        if itpmLimit > 0 and used_itpm + input  > itpmLimit then return 0 end
        if otpmLimit > 0 and used_otpm + output > otpmLimit then return 0 end
        if concLimit > 0 and inflight  + 1      > concLimit then return 0 end

        if dayTokens > 0 then
          local spentTokens = tonumber(redis.call('GET', KEYS[5]) or '0')
          if spentTokens + input + output > dayTokens then return -1 end
        end

        if dayUsd > 0 then
          local spentUsd = tonumber(redis.call('GET', KEYS[6]) or '0')
          if spentUsd + cost > dayUsd then return -1 end
        end

        redis.call('ZADD', KEYS[1], now, token .. '|1')
        redis.call('ZADD', KEYS[2], now, token .. '|' .. input)
        redis.call('ZADD', KEYS[3], now, token .. '|' .. output)
        redis.call('ZADD', KEYS[4], now, token)

        for i = 1, 4 do redis.call('PEXPIRE', KEYS[i], window + ttl) end

        return 1
        """;

    /// <summary>
    /// Swap the estimate for the truth. KEYS: itpm, otpm, concurrency, daily-tokens, daily-usd.
    /// ARGV: now, token, actualInput, actualOutput, cost, dailyTtl(s).
    /// </summary>
    private const string ReconcileScript = """
        local now    = tonumber(ARGV[1])
        local token  = ARGV[2]
        local input  = tonumber(ARGV[3])
        local output = tonumber(ARGV[4])
        local cost   = tonumber(ARGV[5])
        local dayTtl = tonumber(ARGV[6])

        local function replace(key, amount)
          local entries = redis.call('ZRANGE', key, 0, -1)
          for _, entry in ipairs(entries) do
            if string.sub(entry, 1, #token + 1) == token .. '|' then
              redis.call('ZREM', key, entry)
            end
          end
          redis.call('ZADD', key, now, token .. '|' .. amount)
        end

        replace(KEYS[1], input)
        replace(KEYS[2], output)
        redis.call('ZREM', KEYS[3], token)

        redis.call('INCRBY', KEYS[4], input + output)
        redis.call('EXPIRE', KEYS[4], dayTtl)

        -- Redis has no decimal type; cost is accumulated in micro-dollars as an integer so
        -- repeated INCRBYFLOAT cannot drift the daily spend ceiling.
        redis.call('INCRBY', KEYS[5], math.floor(cost * 1000000 + 0.5))
        redis.call('EXPIRE', KEYS[5], dayTtl)

        return 1
        """;

    /// <summary>
    /// Give back an unspent reservation. A script rather than a read-then-write batch: the
    /// entries have to be found by prefix and removed in the same round trip, and a batch cannot
    /// read its own results before it is executed.
    /// KEYS: rpm, itpm, otpm, concurrency. ARGV: token.
    /// </summary>
    private const string ReleaseScript = """
        local token = ARGV[1]
        local prefix = token .. '|'

        for i = 1, 3 do
          local entries = redis.call('ZRANGE', KEYS[i], 0, -1)
          for _, entry in ipairs(entries) do
            if string.sub(entry, 1, #prefix) == prefix then
              redis.call('ZREM', KEYS[i], entry)
            end
          end
        end

        redis.call('ZREM', KEYS[4], token)
        return 1
        """;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(10);

    private readonly RoutingOptions _options = options.Value;

    public async Task<BudgetReservation?> TryReserveAsync(BudgetRequest request, CancellationToken ct = default)
    {
        var pool = registry.Pools.FirstOrDefault(p => p.Key == request.Pool);
        if (pool is null)
        {
            // An unknown pool is a configuration error, not a budget decision. Refusing here keeps
            // an unbudgeted model from quietly getting unlimited throughput.
            logger.LogError("Pool '{Pool}' is not in the registry; refusing to reserve.", request.Pool);
            return null;
        }

        var token = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var limits = await EffectiveLimitsAsync(pool);

        var result = (int)await Database().ScriptEvaluateAsync(
            ReserveScript,
            Keys(pool.Key),
            [
                now.ToUnixTimeMilliseconds(),
                (long)Window.TotalMilliseconds,
                token,
                request.EstimatedInputTokens,
                request.MaxOutputTokens,
                (double)request.EstimatedCostUsd,
                limits.Rpm,
                limits.Itpm,
                limits.Otpm,
                limits.Concurrency,
                pool.DailyTokens,
                (double)pool.DailyUsd,
                (long)ReservationTtl.TotalMilliseconds,
            ]);

        if (result == -1)
        {
            logger.LogWarning("Pool '{Pool}' has hit its daily ceiling; no further calls today.", pool.Key);
            return null;
        }

        return result == 1 ? new BudgetReservation(pool.Key, token, now.Add(ReservationTtl)) : null;
    }

    public async Task ReconcileAsync(
        BudgetReservation reservation, int inputTokens, int outputTokens, decimal costUsd, CancellationToken ct = default)
    {
        var keys = Keys(reservation.Pool);
        await Database().ScriptEvaluateAsync(
            ReconcileScript,
            [keys[1], keys[2], keys[3], keys[4], keys[5]],
            [
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                reservation.Token,
                inputTokens,
                outputTokens,
                (double)costUsd,
                (long)TimeSpan.FromDays(2).TotalSeconds,
            ]);
    }

    public async Task ReleaseAsync(BudgetReservation reservation, CancellationToken ct = default)
    {
        var keys = Keys(reservation.Pool);
        await Database().ScriptEvaluateAsync(
            ReleaseScript,
            [keys[0], keys[1], keys[2], keys[3]],
            [reservation.Token]);
    }

    public async Task<PoolHeadroom> GetHeadroomAsync(string pool, CancellationToken ct = default)
    {
        var descriptor = registry.Pools.FirstOrDefault(p => p.Key == pool);
        if (descriptor is null)
        {
            return PoolHeadroom.Closed(pool);
        }

        if (await IsCircuitOpenAsync(pool, ct))
        {
            return PoolHeadroom.Closed(pool);
        }

        var database = Database();
        var keys = Keys(pool);
        var limits = await EffectiveLimitsAsync(descriptor);
        var cutoff = DateTimeOffset.UtcNow.Subtract(Window).ToUnixTimeMilliseconds();

        var usedRequests = await SumWindowAsync(database, keys[0], cutoff);
        var usedInput = await SumWindowAsync(database, keys[1], cutoff);
        var usedOutput = await SumWindowAsync(database, keys[2], cutoff);
        var inflight = await database.SortedSetLengthAsync(keys[3]);

        // The tightest dimension is the one that will actually block, so it is the headroom.
        var fractions = new List<double>();
        AddFraction(fractions, usedRequests, limits.Rpm);
        AddFraction(fractions, usedInput, limits.Itpm);
        AddFraction(fractions, usedOutput, limits.Otpm);
        AddFraction(fractions, inflight, limits.Concurrency);

        return new PoolHeadroom(
            pool,
            fractions.Count == 0 ? 1 : fractions.Min(),
            CircuitOpen: false,
            (int)limits.Concurrency);
    }

    public async Task ObserveLimitsAsync(
        string pool, int? remainingRequests, long? remainingTokens, TimeSpan? resetAfter, CancellationToken ct = default)
    {
        var descriptor = registry.Pools.FirstOrDefault(p => p.Key == pool);
        if (descriptor is null || !descriptor.LearnFromHeaders)
        {
            return;
        }

        var database = Database();
        var ttl = resetAfter ?? TimeSpan.FromMinutes(5);

        if (remainingRequests is { } requests)
        {
            await database.StringSetAsync($"{Prefix}{pool}:learned:rpm", requests, ttl);
        }

        if (remainingTokens is { } tokens)
        {
            await database.StringSetAsync($"{Prefix}{pool}:learned:tpm", tokens, ttl);
        }
    }

    public async Task RecordOutcomeAsync(
        string pool, bool success, bool rateLimited, int latencyMs, CancellationToken ct = default)
    {
        var database = Database();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resilience = _options.Resilience;

        var outcomes = $"{Prefix}{pool}:outcomes";
        await database.SortedSetAddAsync(outcomes, $"{now}:{Guid.NewGuid():N}|{(success ? 1 : 0)}|{latencyMs}", now);
        await database.SortedSetRemoveRangeByScoreAsync(outcomes, double.NegativeInfinity, now - (long)TimeSpan.FromMinutes(5).TotalMilliseconds);
        await database.KeyExpireAsync(outcomes, TimeSpan.FromMinutes(10));

        if (success)
        {
            await database.StringSetAsync($"{Prefix}{pool}:consecutive-failures", 0);
            await AdaptUpAsync(database, pool, latencyMs);
            return;
        }

        var consecutive = await database.StringIncrementAsync($"{Prefix}{pool}:consecutive-failures");
        await database.KeyExpireAsync($"{Prefix}{pool}:consecutive-failures", TimeSpan.FromMinutes(10));

        if (rateLimited)
        {
            await AdaptDownAsync(database, pool);
        }

        var recent = await RecentOutcomesAsync(database, now);
        var failureRatio = recent.Count >= resilience.CircuitSampleSize
            ? recent.Count(o => !o.Success) / (double)recent.Count
            : 0;

        if (consecutive >= resilience.CircuitConsecutiveFailures || failureRatio >= resilience.CircuitFailureRatio)
        {
            await database.StringSetAsync($"{Prefix}{pool}:circuit", "open", resilience.CircuitBreakDuration);
            logger.LogWarning(
                "Circuit opened for pool '{Pool}' ({Consecutive} consecutive failures, {Ratio:P0} of the last {Count})",
                pool, consecutive, failureRatio, recent.Count);
        }
    }

    public async Task<bool> IsCircuitOpenAsync(string pool, CancellationToken ct = default) =>
        await Database().KeyExistsAsync($"{Prefix}{pool}:circuit");

    public async Task<PoolStatistics> GetStatisticsAsync(string pool, CancellationToken ct = default)
    {
        var database = Database();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var outcomes = await RecentOutcomesAsync(database, now, pool);

        var spentMicros = (long?)await database.StringGetAsync(DailyUsdKey(pool)) ?? 0;

        if (outcomes.Count == 0)
        {
            return PoolStatistics.Unknown with { SpentTodayUsd = spentMicros / 1_000_000m };
        }

        var latencies = outcomes.Select(o => o.LatencyMs).Order().ToList();
        var p95Index = Math.Min(latencies.Count - 1, (int)Math.Ceiling(latencies.Count * 0.95) - 1);

        return new PoolStatistics(
            outcomes.Count(o => !o.Success) / (double)outcomes.Count,
            latencies[Math.Max(0, p95Index)],
            spentMicros / 1_000_000m);
    }

    /// <summary>
    /// AIMD, the "additive increase" half (plan §14.4): after enough successes at healthy latency,
    /// try one more concurrent call. Slow growth is the point — the limit is being discovered, and
    /// overshooting it costs a 429 for every call in flight.
    /// </summary>
    private async Task AdaptUpAsync(IDatabase database, string pool, int latencyMs)
    {
        var descriptor = registry.Pools.FirstOrDefault(p => p.Key == pool);
        if (descriptor is not { Adaptive: true })
        {
            return;
        }

        const int successesPerStep = 20;
        const int latencyTargetMs = 20_000;

        if (latencyMs > latencyTargetMs)
        {
            return;
        }

        var successes = await database.StringIncrementAsync($"{Prefix}{pool}:adaptive-successes");
        if (successes < successesPerStep)
        {
            return;
        }

        await database.StringSetAsync($"{Prefix}{pool}:adaptive-successes", 0);
        var current = await CurrentAdaptiveConcurrencyAsync(database, pool, descriptor);
        await database.StringSetAsync(
            $"{Prefix}{pool}:adaptive-concurrency",
            Math.Min(descriptor.MaxConcurrency, current + 1));
    }

    /// <summary>The "multiplicative decrease" half: on a 429, halve immediately.</summary>
    private async Task AdaptDownAsync(IDatabase database, string pool)
    {
        var descriptor = registry.Pools.FirstOrDefault(p => p.Key == pool);
        if (descriptor is not { Adaptive: true })
        {
            return;
        }

        var current = await CurrentAdaptiveConcurrencyAsync(database, pool, descriptor);
        await database.StringSetAsync($"{Prefix}{pool}:adaptive-concurrency", Math.Max(1, current / 2));
        await database.StringSetAsync($"{Prefix}{pool}:adaptive-successes", 0);
    }

    private static async Task<int> CurrentAdaptiveConcurrencyAsync(
        IDatabase database, string pool, PoolDescriptor descriptor)
    {
        var value = await database.StringGetAsync($"{Prefix}{pool}:adaptive-concurrency");
        return value.HasValue ? (int)value : descriptor.MaxConcurrency;
    }

    private async Task<(long Rpm, long Itpm, long Otpm, long Concurrency)> EffectiveLimitsAsync(PoolDescriptor pool)
    {
        var database = Database();
        var safety = _options.SafetyFactor;

        var rpm = (long)(pool.Rpm * safety);
        var itpm = (long)(pool.Itpm * safety);
        var otpm = (long)(pool.Otpm * safety);
        long concurrency = pool.MaxConcurrency;

        if (pool.LearnFromHeaders)
        {
            // The learned value is what the provider says is left right now, so it is a ceiling on
            // the configured one rather than a replacement for it.
            var learnedRpm = await database.StringGetAsync($"{Prefix}{pool.Key}:learned:rpm");
            if (learnedRpm.HasValue)
            {
                rpm = rpm == 0 ? (long)learnedRpm : Math.Min(rpm, (long)learnedRpm);
            }

            var learnedTpm = await database.StringGetAsync($"{Prefix}{pool.Key}:learned:tpm");
            if (learnedTpm.HasValue)
            {
                itpm = itpm == 0 ? (long)learnedTpm : Math.Min(itpm, (long)learnedTpm);
            }
        }

        if (pool.Adaptive)
        {
            concurrency = await CurrentAdaptiveConcurrencyAsync(database, pool.Key, pool);
        }

        return (rpm, itpm, otpm, concurrency);
    }

    private sealed record Outcome(bool Success, int LatencyMs);

    private async Task<List<Outcome>> RecentOutcomesAsync(IDatabase database, long now, string? pool = null)
    {
        var key = $"{Prefix}{pool ?? string.Empty}:outcomes";
        var entries = await database.SortedSetRangeByScoreAsync(
            key, now - (long)TimeSpan.FromMinutes(5).TotalMilliseconds, now);

        var outcomes = new List<Outcome>(entries.Length);
        foreach (var entry in entries)
        {
            var parts = ((string?)entry)?.Split('|');
            if (parts is { Length: 3 }
                && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var success)
                && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var latency))
            {
                outcomes.Add(new Outcome(success == 1, latency));
            }
        }

        return outcomes;
    }

    private static async Task<double> SumWindowAsync(IDatabase database, RedisKey key, long cutoff)
    {
        await database.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, cutoff);
        var entries = await database.SortedSetRangeByRankAsync(key);

        var total = 0d;
        foreach (var entry in entries)
        {
            var text = (string?)entry;
            var bar = text?.LastIndexOf('|') ?? -1;
            if (bar >= 0 && double.TryParse(text![(bar + 1)..], CultureInfo.InvariantCulture, out var amount))
            {
                total += amount;
            }
        }

        return total;
    }

    private static void AddFraction(List<double> fractions, double used, long limit)
    {
        if (limit > 0)
        {
            fractions.Add(Math.Clamp(1 - used / limit, 0, 1));
        }
    }

    private static RedisKey[] Keys(string pool) =>
    [
        $"{Prefix}{pool}:rpm",
        $"{Prefix}{pool}:itpm",
        $"{Prefix}{pool}:otpm",
        $"{Prefix}{pool}:concurrency",
        $"{Prefix}{pool}:daily:tokens:{Today}",
        DailyUsdKey(pool),
    ];

    private static RedisKey DailyUsdKey(string pool) => $"{Prefix}{pool}:daily:usd:{Today}";

    private static string Today => DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private IDatabase Database() => redis.GetDatabase();
}
