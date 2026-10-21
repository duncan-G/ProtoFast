using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

public sealed class HistoricalStoryOptions
{
    public string ResearchModelClass { get; set; } = ModelClasses.Medium;

    public string WritingModelClass { get; set; } = ModelClasses.Large;

    /// <summary>Pins both stages to one provider instead of the configured default.</summary>
    public string? Provider { get; set; }

    /// <summary>Model turns the research may take before it must have submitted its brief.</summary>
    public int MaxResearchTurns { get; set; } = 30;

    /// <summary>Source text the screenwriter gets alongside the brief.</summary>
    public int MaxSourceChars { get; set; } = 48_000;

    public int MaxConcurrentRuns { get; set; } = 1;

    public int MaxAttempts { get; set; } = 2;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>A run whose worker has not beaten for this long is taken over by another.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(2);
}
