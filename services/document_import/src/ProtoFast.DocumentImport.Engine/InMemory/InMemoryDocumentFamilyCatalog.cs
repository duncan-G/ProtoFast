using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryDocumentFamilyCatalog : IDocumentFamilyCatalog
{
    private readonly ConcurrentDictionary<string, Entry> _families = new(StringComparer.Ordinal);

    public Task AddExecutorAsync(string family, ExecutorRef executor, CancellationToken ct)
    {
        var entry = EntryFor(family);
        lock (entry)
        {
            if (!entry.Executors.Contains(executor))
            {
                entry.Executors.Add(executor);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutorRef>> ExecutorsAsync(string family, CancellationToken ct)
    {
        var entry = EntryFor(family);
        lock (entry)
        {
            return Task.FromResult<IReadOnlyList<ExecutorRef>>(entry.Executors.ToList());
        }
    }

    public Task AddVerifierAsync(string family, VerifierSpec spec, CancellationToken ct)
    {
        var entry = EntryFor(family);
        lock (entry)
        {
            if (entry.Verifiers.Any(v => v.Id == spec.Id))
            {
                throw new InvalidOperationException($"Verifier '{spec.Id}' is already defined in document family '{family}'.");
            }

            entry.Verifiers.Add(spec);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VerifierSpec>> VerifiersAsync(string family, CancellationToken ct)
    {
        var entry = EntryFor(family);
        lock (entry)
        {
            return Task.FromResult<IReadOnlyList<VerifierSpec>>(entry.Verifiers.ToList());
        }
    }

    private Entry EntryFor(string family) => _families.GetOrAdd(family, _ => new Entry());

    private sealed class Entry
    {
        public List<ExecutorRef> Executors { get; } = [];
        public List<VerifierSpec> Verifiers { get; } = [];
    }
}
