namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// A fix the structure orchestrator had to make by hand, recorded as evidence that something
/// upstream is missing (orchestrator plan §12.3(d)).
///
/// <para>Gaps are <em>written and never read back</em>. Nothing in the pipeline injects them into
/// a prompt, which is the whole of what keeps this outside the blast radius of plan §24.1: an
/// instinct reaches a model and therefore has to derive from human corrections, whereas a gap
/// reaches a person and a report. The loop closes through git — cluster the rows, turn a cluster
/// into a PR, let CI's eval gate qualify it — which is slower than self-modification and the only
/// path that ends with a qualified model and a working rollback.</para>
///
/// <para>The text is model-authored and is rendered to people, which is exactly the audience
/// prompt injection targets when it cannot reach a prompt. <see cref="Evidence"/> holds node
/// references rather than quoted document text, the free-text fields are capped, and the UI that
/// renders them treats them as untrusted input.</para>
/// </summary>
public sealed class CapabilityGap
{
    public long Id { get; set; }

    /// <summary><c>deterministic</c>, <c>agent</c> or <c>prompt</c>; anything else is not stored.</summary>
    public required string Kind { get; set; }

    /// <summary>Which role's work the gap was observed in.</summary>
    public required string Role { get; set; }

    /// <summary>What happened, as a statement about the pipeline rather than about the document.</summary>
    public required string Observation { get; set; }

    /// <summary>What should have existed so the orchestrator would not have had to intervene.</summary>
    public required string Proposal { get; set; }

    public required string RunId { get; set; }

    /// <summary>Node references, comma-separated. Never paragraph text, and never a paragraph id.</summary>
    public required string Evidence { get; set; }

    public string? DocumentFamily { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
