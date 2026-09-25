using System.Collections.Concurrent;

namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Resolves verifier ids. A verifier registered in code wins, so a distilled deterministic
/// verifier replaces the rubric-judged one of the same id; otherwise the id is looked up among
/// the bucket's agent-defined <see cref="VerifierSpec"/>s.
/// </summary>
public sealed class VerifierCatalog(
    IEnumerable<IVerifier> registered,
    IBucketCatalog buckets,
    IRubricVerifierFactory? rubrics = null)
{
    private readonly Dictionary<string, IVerifier> _registered =
        registered.ToDictionary(v => v.Id, StringComparer.Ordinal);

    private readonly ConcurrentDictionary<VerifierSpec, IVerifier> _rubricVerifiers = new();

    public async Task<IReadOnlyList<IVerifier>> ResolveAsync(
        string bucket, IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        IReadOnlyList<VerifierSpec>? specs = null;
        var resolved = new List<IVerifier>(ids.Count);
        foreach (var id in ids)
        {
            if (_registered.TryGetValue(id, out var verifier))
            {
                resolved.Add(verifier);
                continue;
            }

            specs ??= await buckets.VerifiersAsync(bucket, ct);
            var spec = specs.FirstOrDefault(s => s.Id == id)
                ?? throw new InvalidOperationException($"Verifier '{id}' is not registered and bucket '{bucket}' does not define it.");
            if (rubrics is null)
            {
                throw new InvalidOperationException($"Verifier '{id}' is rubric-judged but no {nameof(IRubricVerifierFactory)} is registered.");
            }

            resolved.Add(_rubricVerifiers.GetOrAdd(spec, rubrics.Create));
        }

        return resolved;
    }

    public async Task<bool> HasDeterministicAsync(string bucket, IReadOnlyList<string> ids, CancellationToken ct) =>
        (await ResolveAsync(bucket, ids, ct)).Any(v => v.IsDeterministic);
}
