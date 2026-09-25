namespace ProtoFast.DocumentImport.Engine;

public sealed class EngineOptions
{
    /// <summary>The stage-scoped agent loop; the Orchestrator rung of every ladder.</summary>
    public ExecutorRef Orchestrator { get; set; } = new("stage-agent", 1);

    public Thresholds Thresholds { get; set; } = new();

    public Budget DiscoveryBudget { get; set; } = Budget.Unbounded;

    /// <summary>Tools an agent-defined executor may use. Null allows any.</summary>
    public IReadOnlySet<string>? AgentToolNames { get; set; }

    /// <summary>Recent discovery runs <c>Context()</c> draws reusable stage ids from.</summary>
    public int ContextRuns { get; set; } = 20;

    public int OutcomePartitions { get; set; } = Math.Max(1, Environment.ProcessorCount);
}
