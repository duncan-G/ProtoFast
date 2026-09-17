using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Routing;

/// <summary>Capabilities a model declares; the router filters on them (plan §14.2).</summary>
[Flags]
public enum ModelCapabilities
{
    None = 0,
    JsonMode = 1,
    StructuredOutput = 2,
    PromptCaching = 4,
    Batch = 8,
}

/// <summary>One model the router may choose. Bound from <c>Seg_Routing__Models</c>.</summary>
public sealed class ModelDescriptor
{
    /// <summary><c>provider/model</c>, e.g. <c>anthropic/claude-sonnet-x</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Budget pool key. Providers commonly pool limits across a model family, so the pool — not
    /// the model — is the unit budgets are tracked against (plan §14.4).
    /// </summary>
    public string LimitPool { get; set; } = string.Empty;

    public ModelTier Tier { get; set; } = ModelTier.Mid;

    public int ContextTokens { get; set; } = 128_000;

    public int MaxOutputTokens { get; set; } = 8_192;

    public List<string> Capabilities { get; set; } = [];

    public decimal InputCostPerMTok { get; set; }

    public decimal OutputCostPerMTok { get; set; }

    /// <summary>
    /// Sensitivity levels this model may see. The enforcement point for the whole data policy
    /// (plan §24.2): the router filters on this first, before anything else.
    /// </summary>
    public List<Sensitivity> AllowedSensitivity { get; set; } = [Sensitivity.Public, Sensitivity.Internal];

    /// <summary>
    /// Roles this model may serve even with no qualification record. Used to bootstrap a new
    /// deployment, where nothing has been qualified yet and the alternative is a router with no
    /// eligible model for any role. Empty in a steady state.
    /// </summary>
    public List<AgentRole> PresumedQualifiedRoles { get; set; } = [];

    public ModelCapabilities ParsedCapabilities =>
        Capabilities.Aggregate(ModelCapabilities.None, (acc, name) =>
            Enum.TryParse<ModelCapabilities>(name, ignoreCase: true, out var value) ? acc | value : acc);

    public decimal CostPerMTok => InputCostPerMTok + OutputCostPerMTok;
}

/// <summary>A rate-limit pool and its configured ceilings (plan §14.4).</summary>
public sealed class PoolDescriptor
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Requests per minute. Zero means "learn from response headers, or adapt".</summary>
    public int Rpm { get; set; }

    /// <summary>Input tokens per minute. Zero means unconfigured.</summary>
    public long Itpm { get; set; }

    /// <summary>Output tokens per minute. Zero means unconfigured.</summary>
    public long Otpm { get; set; }

    /// <summary>Hard daily ceiling in tokens, or zero for none.</summary>
    public long DailyTokens { get; set; }

    /// <summary>Hard daily spend ceiling in USD, or zero for none (plan §29.4).</summary>
    public decimal DailyUsd { get; set; }

    public int MaxConcurrency { get; set; } = 16;

    /// <summary>The provider returns remaining-quota headers; use them to correct the limits.</summary>
    public bool LearnFromHeaders { get; set; }

    /// <summary>The provider returns nothing useful; find the limit by AIMD instead (plan §14.4).</summary>
    public bool Adaptive { get; set; }
}

/// <summary>
/// The model catalogue, with qualification applied. Agents ask for a capability; only this decides
/// which model serves it (plan §14.1).
/// </summary>
public interface IModelRegistry
{
    IReadOnlyList<ModelDescriptor> Models { get; }

    IReadOnlyList<PoolDescriptor> Pools { get; }

    ModelDescriptor? Find(string key);

    PoolDescriptor PoolFor(ModelDescriptor model);

    /// <summary>
    /// Whether this model is qualified for this role at this prompt version. A prompt change
    /// invalidates qualification until it is re-evaluated (plan §14.2) — which is enforced by the
    /// prompt version being part of the lookup key, not by anyone remembering to clear a flag.
    /// </summary>
    Task<bool> IsQualifiedAsync(ModelDescriptor model, AgentRole role, string promptVersion, CancellationToken ct = default);
}
