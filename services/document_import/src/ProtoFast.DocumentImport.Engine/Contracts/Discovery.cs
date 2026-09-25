namespace ProtoFast.DocumentImport.Engine;

public enum RunMode
{
    Discovery,   // agent loop owns control flow; engine provides tools and records
    Scheduled    // engine walks a mined WorkflowDefinition; agent is one executor among tiers
}

public sealed record BucketPolicy(
    string Bucket,
    RunMode Mode,
    WorkflowRef? Workflow,        // present once a mined definition has been promoted
    Confidence Confidence,        // pass rate of the scheduled run's terminal output
    DateTimeOffset UpdatedAt)     // decay reference, as on PolicyRow
{
    /// <summary>Every bucket starts here.</summary>
    public static BucketPolicy Default(string bucket, DateTimeOffset at) =>
        new(bucket, RunMode.Discovery, null, Confidence.Prior, at);
}

public interface IBucketPolicyStore
{
    Task<BucketPolicy> GetAsync(string bucket, CancellationToken ct);
    Task PutAsync(BucketPolicy policy, CancellationToken ct);
}

/// <summary>
/// The engine's primitives, handed to the agent loop as tools, so nothing the agent does is
/// invisible. The DAG is not declared, it is recorded: the inputs a write or delegation names
/// become its stage's dependency edges.
/// </summary>
public interface IAgentTools
{
    Task<BucketContext> Context();                   // stage ids, executors, verifiers earlier runs used here
    Task<Stream>        ReadArtifact(ArtifactRef reference);

    // `inputs` records which artifacts the output was derived from; that is the only way a stage
    // the agent does itself gets dependency edges for the miner.
    Task<WriteResult>   WriteArtifact(
        string stageId, Stream content, ContractRef contract, IReadOnlyList<ArtifactRef>? inputs = null);

    Task<ExecutorRef>   DefineExecutor(ExecutorSpec spec);   // validated (tier, tools, assembly builds); usable now
    Task<string>        DefineVerifier(VerifierSpec spec);   // rubric-judged until a deterministic one is distilled

    // `output` is the contract the delegate must satisfy. When omitted, the contract this bucket
    // last recorded for the stage is used.
    Task<StageRecord>   Delegate(
        string stageId, ExecutorRef executor, IReadOnlyList<ArtifactRef> inputs, ContractRef? output = null);

    Task                Record(Decision decision);
}

public sealed record BucketContext(
    IReadOnlyList<string> StageIds,
    IReadOnlyList<ExecutorSpec> Executors,
    IReadOnlyList<VerifierSpec> Verifiers);

public sealed record VerifierSpec(string Id, string StageId, string Rubric);

public sealed record WriteResult(ArtifactRef Ref, IReadOnlyList<VerifierResult> Verdicts);

/// <summary>
/// The discovery-mode orchestrator: an agent loop that owns control flow and works only through
/// <see cref="IAgentTools"/>. Model-backed, so it lives outside the engine.
///
/// <para>The engine mints <paramref name="trace"/> before the loop starts, because every
/// <c>WriteArtifact</c> is an <see cref="Tier.Orchestrator"/> record and must carry a trace. The
/// agent stores its transcript under it.</para>
/// </summary>
public interface IDiscoveryAgent
{
    Task RunAsync(ArtifactRef input, IAgentTools tools, TraceRef trace, CancellationToken ct);

    /// <summary>
    /// The same loop scoped to one stage, as the <see cref="Tier.Orchestrator"/> executor in
    /// scheduled mode: same tools, same rules, but it can only write that stage's output. The
    /// stage output is whatever it wrote last.
    /// </summary>
    Task RunStageAsync(StageRequest request, IAgentTools tools, TraceRef trace, CancellationToken ct);
}

public sealed record MinedWorkflow(WorkflowDefinition Workflow, IReadOnlyList<PolicyRow> Seeds);

public interface IWorkflowMiner
{
    // Stage ids, contracts and dependency edges present in at least MinSupport of the runs form
    // the DAG. Per stage, the executor the agent delegated to most seeds the ladder and becomes
    // the primary: its discovery StageRecords are its track record. Stages the agent always did
    // itself stay at Orchestrator.
    Task<MinedWorkflow?> MineAsync(string bucket, IReadOnlyList<RunSummary> runs, CancellationToken ct);
}

/// <summary>
/// Everything a bucket has defined in discovery mode: agent-defined executors and verifier specs.
/// An agent-defined executor is scoped to its bucket until the miner carries it into a workflow.
/// </summary>
public interface IBucketCatalog
{
    Task AddExecutorAsync(string bucket, ExecutorRef executor, CancellationToken ct);
    Task<IReadOnlyList<ExecutorRef>> ExecutorsAsync(string bucket, CancellationToken ct);
    Task AddVerifierAsync(string bucket, VerifierSpec spec, CancellationToken ct);
    Task<IReadOnlyList<VerifierSpec>> VerifiersAsync(string bucket, CancellationToken ct);
}

/// <summary>A mined workflow waiting for a human, with the policy rows promotion will seed.</summary>
public interface IMinedWorkflowStore
{
    Task PutAsync(string bucket, MinedWorkflow mined, CancellationToken ct);
    Task<(string Bucket, MinedWorkflow Mined)?> GetAsync(WorkflowRef reference, CancellationToken ct);
}
