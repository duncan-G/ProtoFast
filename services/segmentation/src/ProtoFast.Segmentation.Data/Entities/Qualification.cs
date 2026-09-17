using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// Whether a model is allowed to do a job (plan §14.2, §26.5). Keyed by prompt version as well as
/// model and role: changing a prompt invalidates the evidence that the model was any good at the
/// old one, so the row simply stops matching and the router stops considering it.
///
/// <para>It lives in Postgres rather than in configuration so every worker gets the same answer
/// the moment a qualification run finishes — and so a rollback to an older image finds its own
/// version's records still there.</para>
/// </summary>
public sealed class Qualification
{
    public long Id { get; set; }

    /// <summary><c>provider/model</c>.</summary>
    public required string ModelKey { get; set; }

    public AgentRole Role { get; set; }

    public required string PromptVersion { get; set; }

    public double Score { get; set; }

    public bool Qualified { get; set; }

    /// <summary>Per-metric detail as JSON, for the report the qualification run writes.</summary>
    public string MetricsJson { get; set; } = "{}";

    public DateTimeOffset EvaluatedAt { get; set; }
}
