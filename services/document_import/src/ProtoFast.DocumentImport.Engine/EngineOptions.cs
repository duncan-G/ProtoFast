namespace ProtoFast.DocumentImport.Engine;

public sealed class EngineOptions
{
    /// <summary>
    /// The seed <see cref="Tier.Orchestrator"/> executor: the stage-scoped agent loop. Every
    /// ladder has it, and every discovery-mode <c>WriteArtifact</c> is recorded against it.
    /// </summary>
    public ExecutorRef Orchestrator { get; set; } = new("stage-agent", 1);

    public Thresholds Thresholds { get; set; } = new();

    /// <summary>The budget a discovery-mode delegation runs under; the agent owns the run's overall spend.</summary>
    public Budget DiscoveryBudget { get; set; } = Budget.Unbounded;

    /// <summary>Tools an agent-defined executor may be given. Null allows any.</summary>
    public IReadOnlySet<string>? AgentToolNames { get; set; }

    /// <summary>How many recent discovery runs <c>Context()</c> reads to offer reusable stage ids.</summary>
    public int ContextRuns { get; set; } = 20;

    /// <summary>Outcome bus partitions; each is drained by exactly one updater loop.</summary>
    public int OutcomePartitions { get; set; } = Math.Max(1, Environment.ProcessorCount);
}
