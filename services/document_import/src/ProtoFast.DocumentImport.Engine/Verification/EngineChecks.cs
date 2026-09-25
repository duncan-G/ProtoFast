namespace ProtoFast.DocumentImport.Engine;

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
