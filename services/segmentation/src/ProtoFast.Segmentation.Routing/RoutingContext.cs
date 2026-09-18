using System.Text.Json;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Routing;

/// <summary>
/// What an agent tells the router about the call it wants (plan §14.5). Notably absent: a
/// provider, or a model. Agents ask for a capability; the router picks.
/// </summary>
public sealed record RoutingContext(
    string RunId,
    string DocumentId,
    PipelinePhase Phase,
    AgentRole Role,
    ModelTier Tier,
    Sensitivity Sensitivity,
    int EstimatedInputTokens,
    int MaxOutputTokens)
{
    /// <summary>
    /// Reviewers pass the producer's provider here so a second opinion comes from somewhere else.
    /// It is a soft preference: an unreviewed document is worse than a same-provider review, so
    /// the filter is dropped if nothing else qualifies.
    /// </summary>
    public string? AvoidProvider { get; init; }

    public bool AllowBatch { get; init; }

    /// <summary>The model pinned for this phase, if the manifest already has one (plan §14.6).</summary>
    public string? PinnedModelKey { get; init; }

    /// <summary>Hash of every prompt asset the role used; qualification is keyed by it.</summary>
    public required string PromptVersion { get; init; }

    /// <summary>Window index or paragraph id — whatever names this unit of work in the ledger.</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// The JSON Schema the reply has to satisfy. Sent as the provider's structured-output
    /// parameter where the chosen model declares <see cref="ModelCapabilities.StructuredOutput"/>,
    /// so the shape is enforced by the decoder rather than requested in prose — the prompt also
    /// renders it, which is what a model without the capability has to work from.
    ///
    /// <para>It is the agent that supplies this, for the same reason the agent supplies a tier:
    /// the artifact it needs back is a property of the job, not of whichever model the router
    /// happens to pick.</para>
    /// </summary>
    public JsonElement? OutputSchema { get; init; }

    /// <summary>Names the schema for providers that want one (<c>tree</c>, <c>review</c>, …).</summary>
    public string? OutputSchemaName { get; init; }
}

/// <summary>What the router chose, and what the call then cost.</summary>
public sealed record RoutingDecision(ModelDescriptor Model, double Score, bool WasPinned, bool SwitchedFromPinned);

/// <summary>Raised when no model can serve a request. Never resolved by downgrading the tier.</summary>
public sealed class NoEligibleModelException(string message) : Exception(message);
