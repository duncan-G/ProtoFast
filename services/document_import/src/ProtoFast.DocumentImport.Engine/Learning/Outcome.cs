namespace ProtoFast.DocumentImport.Engine;

public sealed record Outcome(
    string Family, string? StageId, ExecutorRef Executor,
    OutcomeKind Kind, double Weight, DateTimeOffset At)
{
    public const double DegradedWeight = 0.5;
    public const double CorrectionWeight = 3;

    public bool IsPass => Kind is OutcomeKind.VerifierPass or OutcomeKind.ShadowPass or OutcomeKind.WorkflowShadowPass;

    public static Outcome From(StageRecord record, string family, DateTimeOffset at) =>
        new(
            family,
            record.StageId,
            record.Executor,
            (record.IsShadow, record.Passed) switch
            {
                (true, true) => OutcomeKind.ShadowPass,
                (true, false) => OutcomeKind.ShadowFail,
                (false, true) => OutcomeKind.VerifierPass,
                (false, false) => OutcomeKind.VerifierFail,
            },
            record.Degraded ? DegradedWeight : 1,
            at);

    public static Outcome Correction(string family, string stageId, ExecutorRef executor, DateTimeOffset at) =>
        new(family, stageId, executor, OutcomeKind.ExternalCorrection, CorrectionWeight, at);

    public static Outcome ForWorkflow(string family, WorkflowRef workflow, bool passed, bool degraded, DateTimeOffset at) =>
        new(
            family,
            null,
            workflow.AsExecutor(),
            passed ? OutcomeKind.WorkflowShadowPass : OutcomeKind.WorkflowShadowFail,
            passed && degraded ? DegradedWeight : 1,
            at);
}
