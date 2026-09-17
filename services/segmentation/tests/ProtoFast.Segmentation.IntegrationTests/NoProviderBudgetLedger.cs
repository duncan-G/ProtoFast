using ProtoFast.Segmentation.Routing.Budgets;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// A budget ledger that refuses every reservation.
///
/// <para>It stands in for Redis so the fixture needs no container, and it doubles as an assertion:
/// the deterministic path must never reach a provider, so any test that gets this far has found a
/// real regression rather than a missing dependency. Refusing rather than throwing is deliberate —
/// a throw here would be indistinguishable from a wiring bug, whereas a refusal surfaces through
/// the router's own "no eligible model" path, which is what a provider-less deployment would
/// actually experience.</para>
/// </summary>
public sealed class NoProviderBudgetLedger : IBudgetLedger
{
    public Task<BudgetReservation?> TryReserveAsync(BudgetRequest request, CancellationToken ct = default) =>
        Task.FromResult<BudgetReservation?>(null);

    public Task ReconcileAsync(
        BudgetReservation reservation, int inputTokens, int outputTokens, decimal costUsd, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task ReleaseAsync(BudgetReservation reservation, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PoolHeadroom> GetHeadroomAsync(string pool, CancellationToken ct = default) =>
        Task.FromResult(PoolHeadroom.Closed(pool));

    public Task ObserveLimitsAsync(
        string pool, int? remainingRequests, long? remainingTokens, TimeSpan? resetAfter, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RecordOutcomeAsync(
        string pool, bool success, bool rateLimited, int latencyMs, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<bool> IsCircuitOpenAsync(string pool, CancellationToken ct = default) => Task.FromResult(true);

    public Task<PoolStatistics> GetStatisticsAsync(string pool, CancellationToken ct = default) =>
        Task.FromResult(PoolStatistics.Unknown);
}
