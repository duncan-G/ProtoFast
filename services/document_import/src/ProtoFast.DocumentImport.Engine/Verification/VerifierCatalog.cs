using System.Collections.Concurrent;

namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Verifiers registered in code take precedence over the document family's rubric-judged specs
/// with the same id.
/// </summary>
public sealed class VerifierCatalog(
    IEnumerable<IVerifier> registered,
    IDocumentFamilyCatalog families,
    IRubricVerifierFactory? rubrics = null)
{
    private readonly Dictionary<string, IVerifier> _registered =
        registered.ToDictionary(v => v.Id, StringComparer.Ordinal);

    private readonly ConcurrentDictionary<VerifierSpec, IVerifier> _rubricVerifiers = new();

    public async Task<IReadOnlyList<IVerifier>> ResolveAsync(
        string family, IReadOnlyList<string> ids, CancellationToken ct)
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

            specs ??= await families.VerifiersAsync(family, ct);
            var spec = specs.FirstOrDefault(s => s.Id == id)
                ?? throw new InvalidOperationException($"Verifier '{id}' is not registered and document family '{family}' does not define it.");
            if (rubrics is null)
            {
                throw new InvalidOperationException($"Verifier '{id}' is rubric-judged but no {nameof(IRubricVerifierFactory)} is registered.");
            }

            resolved.Add(_rubricVerifiers.GetOrAdd(spec, rubrics.Create));
        }

        return resolved;
    }

    public async Task<bool> HasDeterministicAsync(string family, IReadOnlyList<string> ids, CancellationToken ct) =>
        (await ResolveAsync(family, ids, ct)).Any(v => v.IsDeterministic);
}
