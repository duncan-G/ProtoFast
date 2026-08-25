using System.Collections.Concurrent;
using ProtoFast.Auth.Api.Correlation;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>In-memory single-use correlation storage, so /signin can run without Redis.</summary>
internal sealed class StubCorrelationStore : ICorrelationStore
{
    private readonly ConcurrentDictionary<string, CorrelationData> _states = new(StringComparer.Ordinal);

    public Task SaveAsync(string state, CorrelationData data, CancellationToken ct = default)
    {
        _states[state] = data;
        return Task.CompletedTask;
    }

    public Task<CorrelationData?> TakeAsync(string state, CancellationToken ct = default) =>
        Task.FromResult(_states.TryRemove(state, out var data) ? data : null);
}
