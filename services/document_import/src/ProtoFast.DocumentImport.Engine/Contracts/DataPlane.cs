namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Content-addressed and immutable. Primary and shadow attempts share inputs by reference, and
/// replaying a run is reading the ledger, never re-executing. Only Codified executors are
/// deterministic; for agent tiers the hash identifies content, it does not predict it.
/// </summary>
public interface IArtifactStore
{
    Task<ArtifactRef> PutAsync(string runId, string stageId, Stream content, ContractRef contract, CancellationToken ct);
    Task<Stream> GetAsync(ArtifactRef reference, CancellationToken ct);

    /// <summary>The contract the artifact was written against.</summary>
    Task<ContractRef> ContractOfAsync(ArtifactRef reference, CancellationToken ct);
}

/// <summary>
/// One attempt at one stage. <see cref="Stage"/> is the definition the attempt ran against: in
/// scheduled mode the workflow's, in discovery mode the one the engine recorded from the agent's
/// call, whose <see cref="StageDefinition.DependsOn"/> is the recorded DAG.
/// </summary>
public sealed record StageRecord(
    string RunId, StageDefinition Stage, IReadOnlyList<ArtifactRef> Inputs, ExecutorRef Executor, Tier Tier,
    StageResult Result, IReadOnlyList<VerifierResult> Verdicts, bool IsShadow)
{
    public string StageId => Stage.Id;
    public ArtifactRef Output => Result.Output;
    public bool Passed => Verdicts.All(v => v.Verdict != Verdict.Fail);
    public bool Degraded => Passed && Verdicts.Any(v => v.Verdict == Verdict.Degraded);
}

public sealed record RunSummary(
    string RunId, Signature Signature, RunMode Mode,
    IReadOnlyList<StageRecord> Stages, TraceRef? Trace,
    IReadOnlyList<Decision> Decisions);       // the loop owner's own decisions, not an executor's

public interface IRunLedger
{
    Task OpenAsync(string runId, Signature signature, RunMode mode, CancellationToken ct);
    Task RecordAsync(StageRecord record, CancellationToken ct);                       // append-only
    Task RecordAsync(string runId, Decision decision, CancellationToken ct);          // append-only
    Task CloseAsync(string runId, TraceRef? trace, CancellationToken ct);
    Task<RunSummary> SummariseAsync(string runId, CancellationToken ct);

    // Closed runs only, newest first.
    Task<IReadOnlyList<RunSummary>> RecentAsync(string bucket, RunMode mode, int take, CancellationToken ct);
    Task<int> CountAsync(string bucket, RunMode mode, CancellationToken ct);
}

public sealed record Playbook(
    PlaybookRef Ref,
    string Instructions,                       // prompt text
    IReadOnlyList<Example> Examples,
    IReadOnlyDictionary<string, string> Rules); // discovered rules, keyed by Decision.Key

/// <summary>
/// The human gate is precise. An <see cref="ExecutorOrigin.AgentDefined"/> executor at an agent
/// tier is <see cref="ExecutorSpec.Promoted"/> on publish: the agent already ran it under verifiers.
/// A <see cref="ExecutorOrigin.Distilled"/> executor, any executor with a
/// <see cref="ExecutorSpec.CodeAssembly"/>, and any mined workflow need <c>PromoteAsync</c> before
/// the scheduler will route to them.
/// </summary>
public interface IRegistry
{
    Task<Playbook>           ResolveAsync(PlaybookRef reference, CancellationToken ct);
    Task<ExecutorSpec>       ResolveAsync(ExecutorRef reference, CancellationToken ct);
    Task<WorkflowDefinition> ResolveAsync(WorkflowRef reference, CancellationToken ct);

    // Publishing assigns the next version for the id; the version on the argument is ignored.
    Task<PlaybookRef> PublishAsync(Playbook playbook, CancellationToken ct);
    Task<ExecutorRef> PublishAsync(ExecutorSpec spec, CancellationToken ct);
    Task<WorkflowRef> PublishAsync(WorkflowDefinition workflow, CancellationToken ct);

    Task PromoteAsync(ExecutorRef reference, CancellationToken ct);   // human gate
    Task PromoteAsync(WorkflowRef reference, CancellationToken ct);   // human gate
    Task<bool> IsPromotedAsync(WorkflowRef reference, CancellationToken ct);
}
