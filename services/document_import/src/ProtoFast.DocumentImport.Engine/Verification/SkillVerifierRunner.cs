namespace ProtoFast.DocumentImport.Engine.Verification;

public sealed class SkillVerifierRunner(IEnumerable<ISkillVerifier> verifiers)
{
    private readonly IReadOnlyList<ISkillVerifier> _verifiers = verifiers.OrderBy(v => v.IsDeterministic ? 0 : 1).ToList();

    public async Task<IReadOnlyList<VerifierResult>> RunAsync(SkillReview review, CancellationToken ct)
    {
        var results = new List<VerifierResult>(_verifiers.Count);
        foreach (var verifier in _verifiers)
        {
            VerifierResult verdict;
            try
            {
                verdict = await verifier.VerifyAsync(review, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
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
