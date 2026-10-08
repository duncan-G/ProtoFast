using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Screenplay.Briefing;

public sealed class RunBriefingOptions
{
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; set; } = 3;

    /// <summary>A claim this old is taken to belong to a briefer that died.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(15);

    public string StepModelClass { get; set; } = ModelClasses.Small;

    public string OverviewModelClass { get; set; } = ModelClasses.Medium;

    /// <summary>Steps go to the step model in chunks of about this many characters.</summary>
    public int MaxChunkChars { get; set; } = 60_000;

    /// <summary>Per tool argument, tool result and agent message.</summary>
    public int MaxTextChars { get; set; } = 1_500;

    public int MaxDiffChars { get; set; } = 8_000;

    /// <summary>Agent text longer than this gets a model brief even when its calls speak for themselves.</summary>
    public int ReasoningChars { get; set; } = 300;
}
