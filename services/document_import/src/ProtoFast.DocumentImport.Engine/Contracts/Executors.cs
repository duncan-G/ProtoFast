namespace ProtoFast.DocumentImport.Engine;

/// <summary>One interface, five tiers. The engine cannot distinguish them.</summary>
public enum Tier
{
    Orchestrator = 0,   // high-thinking agent does the work and reviews it
    DelegateLarge = 1,  // large-model subagent, agent sees only the verifier summary
    DelegateMedium = 2, // mid-size model subagent
    DelegateSmall = 3,  // small-model subagent
    Codified = 4        // deterministic code, distilled from the tiers above
}

public sealed record StageRequest(
    string RunId,
    StageDefinition Stage,
    Signature Signature,
    IReadOnlyList<ArtifactRef> Inputs);

public sealed record StageResult(
    ArtifactRef Output,
    TraceRef? Trace,          // reasoning trace; the engine rejects an Orchestrator result without one
    Cost Cost,
    IReadOnlyList<Decision> Decisions);   // named choices the executor made, for distillation

public interface IExecutor
{
    Tier Tier { get; }
    Task<StageResult> ExecuteAsync(StageRequest request, CancellationToken ct);
}

public interface IExecutorResolver
{
    // Builds a runnable executor from its spec: model + playbook + tools for agent tiers,
    // a loaded assembly for Codified. Refs are immutable, so resolved executors are cached
    // for the process lifetime.
    Task<IExecutor> ResolveAsync(ExecutorRef reference, CancellationToken ct);
}

// An executor is data. The playbook lives on the spec, not on the request.
public sealed record ExecutorSpec(
    ExecutorRef Ref,
    Tier Tier,
    string? ModelClass,                  // large | medium | small; null for Codified
    PlaybookRef? Playbook,               // instructions, examples, rules; null for Codified
    IReadOnlyList<string> Tools,         // agent tools this executor may call; empty for Codified
    string? CodeAssembly,                // present for Codified
    ExecutorOrigin Origin,
    bool Promoted);                      // human gate; see IRegistry

public enum ExecutorOrigin { Seed, AgentDefined, Distilled }

/// <summary>
/// The unit of learning. An executor names what it chose and why, in a form the distiller can
/// turn into a playbook rule.
/// </summary>
public sealed record Decision(string Key, string Choice, string Rationale, double Confidence);

public static class ModelClasses
{
    public const string Large = "large";
    public const string Medium = "medium";
    public const string Small = "small";

    /// <summary>The model class an agent tier runs on; null for tiers that are not a delegate.</summary>
    public static string? For(Tier tier) => tier switch
    {
        Tier.DelegateLarge => Large,
        Tier.DelegateMedium => Medium,
        Tier.DelegateSmall => Small,
        _ => null,
    };
}
