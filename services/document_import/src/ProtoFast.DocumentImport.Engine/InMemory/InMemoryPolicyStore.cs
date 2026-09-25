using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryPolicyStore(EngineOptions options, TimeProvider time) : IPolicyStore
{
    private readonly ConcurrentDictionary<(string Family, string StageId), PolicyRow> _rows = new();

    public Task<IReadOnlyDictionary<string, PolicyRow>> SnapshotAsync(
        string family, IEnumerable<string> stageIds, CancellationToken ct)
    {
        IReadOnlyDictionary<string, PolicyRow> snapshot = stageIds
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => Get(family, id), StringComparer.Ordinal);
        return Task.FromResult(snapshot);
    }

    public Task<PolicyRow> GetAsync(string family, string stageId, CancellationToken ct) =>
        Task.FromResult(Get(family, stageId));

    public Task PutAsync(PolicyRow row, CancellationToken ct)
    {
        _rows[(row.Family, row.StageId)] = row;
        return Task.CompletedTask;
    }

    private PolicyRow Get(string family, string stageId) =>
        _rows.TryGetValue((family, stageId), out var row)
            ? row
            : PolicyRow.Default(family, stageId, options.Orchestrator, time.GetUtcNow());
}
