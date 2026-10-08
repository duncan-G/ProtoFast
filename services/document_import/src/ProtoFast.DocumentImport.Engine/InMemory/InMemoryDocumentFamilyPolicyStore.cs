using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryDocumentFamilyPolicyStore(TimeProvider time) : IDocumentFamilyPolicyStore
{
    private readonly ConcurrentDictionary<string, DocumentFamilyPolicy> _policies = new(StringComparer.Ordinal);

    public Task<DocumentFamilyPolicy> GetAsync(string family, CancellationToken ct) =>
        Task.FromResult(
            _policies.TryGetValue(family, out var policy) ? policy : DocumentFamilyPolicy.Default(family, time.GetUtcNow()));

    public Task PutAsync(DocumentFamilyPolicy policy, CancellationToken ct)
    {
        _policies[policy.Family] = policy;
        return Task.CompletedTask;
    }
}
