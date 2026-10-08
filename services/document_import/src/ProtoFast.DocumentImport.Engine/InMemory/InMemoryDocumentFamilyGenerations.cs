using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryDocumentFamilyGenerations : IDocumentFamilyGenerations
{
    private readonly ConcurrentDictionary<string, int> _generations = new(StringComparer.Ordinal);

    public Task<int> CurrentAsync(string family, CancellationToken ct) =>
        Task.FromResult(_generations.GetValueOrDefault(family));

    public Task<int> ResetAsync(string family, CancellationToken ct) =>
        Task.FromResult(_generations.AddOrUpdate(family, 1, (_, generation) => generation + 1));
}
