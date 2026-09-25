namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Orders deterministic verifiers first and stops at the first <see cref="Verdict.Fail"/>, so a
/// schema violation never pays for a model-backed judgement.
///
/// <para>Before any verifier runs the engine applies its own checks, which are not optional and
/// not the stage's to configure: an attempt must produce an output, stay inside the stage's cost
/// budget, and, at <see cref="Tier.Orchestrator"/>, carry a reasoning trace.</para>
/// </summary>
public sealed class VerifierRunner(VerifierCatalog catalog)
{
    public async Task<IReadOnlyList<VerifierResult>> RunAsync(
        StageRequest request, StageResult result, Tier tier, CancellationToken ct)
    {
        if (EngineChecks.Check(request, result, tier) is { } rejected)
        {
            return [rejected];
        }

        var verifiers = await catalog.ResolveAsync(request.Signature.Bucket, request.Stage.Verifiers, ct);
        var results = new List<VerifierResult>(verifiers.Count);

        // OrderBy is stable, so the stage's declared order holds within each group.
        foreach (var verifier in verifiers.OrderBy(v => v.IsDeterministic ? 0 : 1))
        {
            VerifierResult verdict;
            try
            {
                verdict = await verifier.VerifyAsync(request, result, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Verifiers own truth; one that cannot judge has not passed anything.
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

public static class EngineChecks
{
    public const string NoOutput = "engine.no-output";
    public const string TraceRequired = "engine.trace-required";
    public const string Budget = "engine.budget";
    public const string ExecutorFaulted = "engine.executor-faulted";

    public static VerifierResult? Check(StageRequest request, StageResult result, Tier tier)
    {
        if (result.Output.IsNone)
        {
            return Fail(NoOutput, "The attempt produced no output artifact.");
        }

        if (tier == Tier.Orchestrator && result.Trace is null)
        {
            return Fail(TraceRequired, "An Orchestrator result must carry a reasoning trace.");
        }

        if (result.Cost.Amount > request.Stage.Budget.MaxCost)
        {
            return Fail(Budget, $"Cost {result.Cost.Amount} exceeds the stage budget of {request.Stage.Budget.MaxCost}.");
        }

        return null;
    }

    public static VerifierResult OverTime(TimeSpan limit) =>
        Fail(Budget, $"The attempt exceeded the stage's {limit} duration budget.");

    public static VerifierResult Faulted(Exception e) =>
        Fail(ExecutorFaulted, $"{e.GetType().Name}: {e.Message}");

    private static VerifierResult Fail(string id, string reason) => new(id, Verdict.Fail, reason, []);
}
