using System.Collections.Concurrent;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryPolicyStore(EngineOptions options, TimeProvider time) : IPolicyStore
{
    private readonly ConcurrentDictionary<(string Bucket, string StageId), PolicyRow> _rows = new();

    public Task<IReadOnlyDictionary<string, PolicyRow>> SnapshotAsync(
        string bucket, IEnumerable<string> stageIds, CancellationToken ct)
    {
        IReadOnlyDictionary<string, PolicyRow> snapshot = stageIds
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => Get(bucket, id), StringComparer.Ordinal);
        return Task.FromResult(snapshot);
    }

    public Task<PolicyRow> GetAsync(string bucket, string stageId, CancellationToken ct) =>
        Task.FromResult(Get(bucket, stageId));

    public Task PutAsync(PolicyRow row, CancellationToken ct)
    {
        _rows[(row.Bucket, row.StageId)] = row;
        return Task.CompletedTask;
    }

    private PolicyRow Get(string bucket, string stageId) =>
        _rows.TryGetValue((bucket, stageId), out var row)
            ? row
            : PolicyRow.Default(bucket, stageId, options.Orchestrator, time.GetUtcNow());
}

public sealed class InMemoryBucketPolicyStore(TimeProvider time) : IBucketPolicyStore
{
    private readonly ConcurrentDictionary<string, BucketPolicy> _policies = new(StringComparer.Ordinal);

    public Task<BucketPolicy> GetAsync(string bucket, CancellationToken ct) =>
        Task.FromResult(
            _policies.TryGetValue(bucket, out var policy) ? policy : BucketPolicy.Default(bucket, time.GetUtcNow()));

    public Task PutAsync(BucketPolicy policy, CancellationToken ct)
    {
        _policies[policy.Bucket] = policy;
        return Task.CompletedTask;
    }
}
