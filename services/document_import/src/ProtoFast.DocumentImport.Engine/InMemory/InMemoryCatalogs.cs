using System.Collections.Concurrent;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryBucketCatalog : IBucketCatalog
{
    private readonly ConcurrentDictionary<string, Entry> _buckets = new(StringComparer.Ordinal);

    public Task AddExecutorAsync(string bucket, ExecutorRef executor, CancellationToken ct)
    {
        var entry = EntryFor(bucket);
        lock (entry)
        {
            if (!entry.Executors.Contains(executor))
            {
                entry.Executors.Add(executor);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutorRef>> ExecutorsAsync(string bucket, CancellationToken ct)
    {
        var entry = EntryFor(bucket);
        lock (entry)
        {
            return Task.FromResult<IReadOnlyList<ExecutorRef>>(entry.Executors.ToList());
        }
    }

    public Task AddVerifierAsync(string bucket, VerifierSpec spec, CancellationToken ct)
    {
        var entry = EntryFor(bucket);
        lock (entry)
        {
            if (entry.Verifiers.Any(v => v.Id == spec.Id))
            {
                throw new InvalidOperationException($"Verifier '{spec.Id}' is already defined in bucket '{bucket}'.");
            }

            entry.Verifiers.Add(spec);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VerifierSpec>> VerifiersAsync(string bucket, CancellationToken ct)
    {
        var entry = EntryFor(bucket);
        lock (entry)
        {
            return Task.FromResult<IReadOnlyList<VerifierSpec>>(entry.Verifiers.ToList());
        }
    }

    private Entry EntryFor(string bucket) => _buckets.GetOrAdd(bucket, _ => new Entry());

    private sealed class Entry
    {
        public List<ExecutorRef> Executors { get; } = [];
        public List<VerifierSpec> Verifiers { get; } = [];
    }
}

public sealed class InMemoryMinedWorkflowStore : IMinedWorkflowStore
{
    private readonly ConcurrentDictionary<WorkflowRef, (string Bucket, MinedWorkflow Mined)> _drafts = new();

    public Task PutAsync(string bucket, MinedWorkflow mined, CancellationToken ct)
    {
        _drafts[mined.Workflow.Ref] = (bucket, mined);
        return Task.CompletedTask;
    }

    public Task<(string Bucket, MinedWorkflow Mined)?> GetAsync(WorkflowRef reference, CancellationToken ct) =>
        Task.FromResult<(string, MinedWorkflow)?>(_drafts.TryGetValue(reference, out var draft) ? draft : null);
}
