using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// One provider call, recorded before the response is handed back to the executor — a crash must
/// not lose a call that was already billed (plan §24.3).
///
/// <para>Document text is deliberately absent. This table is the audit trail proving which
/// provider saw which run at which sensitivity, and it is queried by people; putting document
/// content in it would turn an audit log into a second copy of the corpus.</para>
/// </summary>
public sealed class ModelCall
{
    public long Id { get; set; }

    public required string RunId { get; set; }

    public PipelinePhase Phase { get; set; }

    public AgentRole Role { get; set; }

    /// <summary>Window index, paragraph id, or similar — whatever identifies the unit of work.</summary>
    public string? Unit { get; set; }

    public required string Provider { get; set; }

    public required string Model { get; set; }

    public required string LimitPool { get; set; }

    /// <summary>Hash of every prompt asset the role used, so a result can be tied to its prompts.</summary>
    public required string PromptVersion { get; set; }

    public Sensitivity Sensitivity { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int CachedInputTokens { get; set; }

    public decimal CostUsd { get; set; }

    public int LatencyMs { get; set; }

    /// <summary><c>ok</c>, or the <c>ProviderErrorKind</c> that ended it.</summary>
    public required string Outcome { get; set; }

    public int Attempt { get; set; }

    public bool Batched { get; set; }

    public DateTimeOffset At { get; set; }
}
