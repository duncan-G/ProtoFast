namespace ProtoFast.Segmentation.Routing.Budgets;

/// <summary>What a call intends to spend from a pool, before it knows what it actually will.</summary>
public sealed record BudgetRequest(
    string Pool,
    int EstimatedInputTokens,
    int MaxOutputTokens,
    decimal EstimatedCostUsd);

/// <summary>
/// A granted reservation. Disposing it without <see cref="ReconcileAsync"/> releases the estimate,
/// so a crashed or cancelled call does not leak budget.
/// </summary>
public sealed record BudgetReservation(string Pool, string Token, DateTimeOffset ExpiresAt);

/// <summary>How much of a pool is free right now, 0..1 over its tightest dimension.</summary>
public sealed record PoolHeadroom(string Pool, double Fraction, bool CircuitOpen, int EffectiveConcurrency)
{
    public static PoolHeadroom Closed(string pool) => new(pool, 0, CircuitOpen: true, 0);
}

/// <summary>
/// Shared rate-limit state across every worker process (plan §14.4).
///
/// <para>It lives in Redis rather than in each worker because a provider's quota is shared by all
/// of them: two workers each staying under the limit locally would together sail past it. Redis on
/// Host B is already a shared in-memory cache with no persistence expectations, which is exactly
/// the right durability — a lost bucket costs one minute of conservatism, not correctness.</para>
/// </summary>
public interface IBudgetLedger
{
    /// <summary>
    /// Atomically reserves capacity across every dimension of a pool, or returns null. All-or-
    /// nothing: a reservation that took request capacity but not token capacity would let a
    /// stampede past the token limit while the request limit still looked healthy.
    /// </summary>
    Task<BudgetReservation?> TryReserveAsync(BudgetRequest request, CancellationToken ct = default);

    /// <summary>
    /// Replaces the estimate with what the call actually used, and records its cost. Always called
    /// on the success path, because an estimate that stays in the bucket is quota nobody can use.
    /// </summary>
    Task ReconcileAsync(
        BudgetReservation reservation, int inputTokens, int outputTokens, decimal costUsd, CancellationToken ct = default);

    /// <summary>Returns the estimate unspent — the call never happened.</summary>
    Task ReleaseAsync(BudgetReservation reservation, CancellationToken ct = default);

    Task<PoolHeadroom> GetHeadroomAsync(string pool, CancellationToken ct = default);

    /// <summary>
    /// Corrects a pool's effective limits from what the provider's own headers reported. The
    /// effective limit is the lower of the configured value and the learned one (plan §14.4).
    /// </summary>
    Task ObserveLimitsAsync(string pool, int? remainingRequests, long? remainingTokens, TimeSpan? resetAfter, CancellationToken ct = default);

    /// <summary>Records a call's outcome, which drives both the circuit breaker and adaptive concurrency.</summary>
    Task RecordOutcomeAsync(string pool, bool success, bool rateLimited, int latencyMs, CancellationToken ct = default);

    Task<bool> IsCircuitOpenAsync(string pool, CancellationToken ct = default);

    /// <summary>Recent error rate and p95 latency for a pool, as the routing score reads them.</summary>
    Task<PoolStatistics> GetStatisticsAsync(string pool, CancellationToken ct = default);
}

public sealed record PoolStatistics(double ErrorRate, int P95LatencyMs, decimal SpentTodayUsd)
{
    public static readonly PoolStatistics Unknown = new(0, 0, 0);
}
