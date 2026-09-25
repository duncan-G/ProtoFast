namespace ProtoFast.DocumentImport.Engine;

public sealed class VerifierRunner(VerifierCatalog catalog)
{
    public async Task<IReadOnlyList<VerifierResult>> RunAsync(
        StageRequest request, StageResult result, Tier tier, CancellationToken ct)
    {
        if (EngineChecks.Check(request, result, tier) is { } rejected)
        {
            return [rejected];
        }

        var verifiers = await catalog.ResolveAsync(request.Signature.Family, request.Stage.Verifiers, ct);
        var results = new List<VerifierResult>(verifiers.Count);

        // OrderBy is stable, so declared order holds within each group.
        foreach (var verifier in verifiers.OrderBy(v => v.IsDeterministic ? 0 : 1))
        {
            VerifierResult verdict;
            try
            {
                verdict = await verifier.VerifyAsync(request, result, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // A verifier that cannot judge has not passed anything.
                verdict = new VerifierResult(verifier.Id, Verdict.Fail, $"Verifier faulted: {e.Message}", []);
            }

            results.Add(verdict);
            if (verdict.Verdict == Verdict.Fail)
            {
                break;
            }
        }

        return results;
    }
}
