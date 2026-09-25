namespace ProtoFast.DocumentImport.Engine;

public enum OutcomeKind
{
    VerifierPass, VerifierFail,                 // primary or escalation attempt
    ShadowPass, ShadowFail,                     // shadow attempt
    ExternalCorrection,                         // a human fixed the output; a fail at triple weight
    WorkflowShadowPass, WorkflowShadowFail      // bucket level, StageId is null
}

/// <summary>
/// Every signal becomes one event type. Outcomes are attributed to an executor, not a tier, so a
/// row can tell its primary, its shadow and an escalation fallback apart.
/// </summary>
public sealed record Outcome(
    string Bucket, string? StageId, ExecutorRef Executor,
    OutcomeKind Kind, double Weight, DateTimeOffset At)      // Weight: 1, or 0.5 for a Degraded pass
{
    public const double DegradedWeight = 0.5;
    public const double CorrectionWeight = 3;

    public bool IsPass => Kind is OutcomeKind.VerifierPass or OutcomeKind.ShadowPass or OutcomeKind.WorkflowShadowPass;

    public static Outcome From(StageRecord record, string bucket, DateTimeOffset at) =>
        new(
            bucket,
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

    /// <summary>A human fixed the output <paramref name="executor"/> produced for the stage.</summary>
    public static Outcome Correction(string bucket, string stageId, ExecutorRef executor, DateTimeOffset at) =>
        new(bucket, stageId, executor, OutcomeKind.ExternalCorrection, CorrectionWeight, at);

    /// <summary>A scheduled run's terminal output, judged by the terminal stages' verifiers.</summary>
    public static Outcome ForWorkflow(string bucket, WorkflowRef workflow, bool passed, bool degraded, DateTimeOffset at) =>
        new(
            bucket,
            null,
            workflow.AsExecutor(),
            passed ? OutcomeKind.WorkflowShadowPass : OutcomeKind.WorkflowShadowFail,
            passed && degraded ? DegradedWeight : 1,
            at);
}

public interface IOutcomeBus
{
    Task PublishAsync(Outcome outcome, CancellationToken ct);
}

public sealed record Thresholds(
    double PromoteAt = 0.95,          // confidence mean to move one tier right
    double DemoteAt = 0.80,           // confidence mean to move one tier left
    int MinObservations = 20,         // effective observations before promotion
    double HalfLifeDays = 30,         // confidence decay
    double ShadowSampleRate = 0.2,    // fraction of runs that also execute the shadow
    int MineAfterRuns = 10,           // discovery runs between mining passes
    double MinSupport = 0.8,          // fraction of runs a stage or edge must appear in
    int MinDemoteObservations = 5)    // effective observations before a primary can be demoted
{
    public bool Ready(Confidence c) => c.Mean >= PromoteAt && c.Observations >= MinObservations;

    // The prior's mean (0.5) is already below DemoteAt. Without some evidence, a tier that was
    // just demoted *to* would be demoted again by its first outcome, pass or fail, and the row
    // would fall straight through to Orchestrator. A promoted tier carries MinObservations, so
    // this never delays demoting one.
    public bool ShouldDemote(Confidence c) => c.Mean < DemoteAt && c.Observations >= MinDemoteObservations;
}

/// <summary>
/// Moves confidence and tiers. The bus partitions by bucket and the updater is a single writer per
/// partition, so no row is ever read-modify-written concurrently.
/// </summary>
public interface IPolicyUpdater
{
    Task ApplyAsync(Outcome outcome, CancellationToken ct);
}

/// <summary>Produces the next rung of a ladder. It writes drafts; a human promotes what needs promoting.</summary>
public interface IDistiller
{
    // Publishes the next-tier candidate for a row. Idempotent per (row, tier): a pending
    // candidate is not requested twice. When the candidate is Promoted it lands in
    // row.Ladder at its tier and becomes row.Shadow.
    //
    //   Orchestrator  -> DelegateLarge   traces + Decisions -> new Playbook + Distilled spec; human gate
    //   DelegateLarge -> Medium -> Small same playbook on the next model class; Promoted on publish
    //   DelegateSmall -> Codified        generated code + tests from the stable playbook; human gate;
    //                                    refused when the stage has no deterministic verifier
    Task RequestCandidateAsync(PolicyRow row, CancellationToken ct);
}
