namespace ProtoFast.Segmentation.Routing;

/// <summary>Bound from <c>Seg_Routing__*</c> (plan §20.3).</summary>
public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    /// <summary>
    /// Configured limits are multiplied by this before use. Provider quotas are enforced against
    /// their own clock, not ours, so aiming for 100% of a limit means occasionally exceeding it.
    /// </summary>
    public double SafetyFactor { get; set; } = 0.9;

    /// <summary>How long a call may wait for headroom before the run goes back on the queue.</summary>
    public TimeSpan MaxRoutingWait { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long to wait for the pinned model before switching (plan §14.6).</summary>
    public TimeSpan StickyMaxWait { get; set; } = TimeSpan.FromMinutes(2);

    public ScoringWeights Weights { get; set; } = new();

    public List<PoolDescriptor> Pools { get; set; } = [];

    public List<ModelDescriptor> Models { get; set; } = [];

    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Per-run spend ceiling in USD; zero disables it. Exceeding it pauses the run for approval
    /// rather than failing it, so the work already paid for is not thrown away (plan §29.4).
    /// </summary>
    public decimal MaxRunCostUsd { get; set; }

    /// <summary>
    /// <c>live</c> calls providers; <c>replay</c> serves recorded responses from
    /// <c>Seg_Providers__ReplayPath</c>, which is what the contract tests use and what makes the
    /// deterministic phases workable with no keys at all (plan §22.2).
    /// </summary>
    public string Mode { get; set; } = "live";
}

public sealed class ScoringWeights
{
    public double Headroom { get; set; } = 0.45;

    public double Cost { get; set; } = 0.25;

    public double Errors { get; set; } = 0.15;

    public double Latency { get; set; } = 0.15;
}

public sealed class ResilienceOptions
{
    public int MaxAttempts { get; set; } = 3;

    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Consecutive failures that open a pool's circuit (plan §14.7).</summary>
    public int CircuitConsecutiveFailures { get; set; } = 5;

    /// <summary>Failure ratio over <see cref="CircuitSampleSize"/> calls that opens the circuit.</summary>
    public double CircuitFailureRatio { get; set; } = 0.5;

    public int CircuitSampleSize { get; set; } = 20;

    public TimeSpan CircuitBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Bound from <c>Seg_Providers__*</c>. Keys arrive from Secrets Manager (plan §21).</summary>
public sealed class ProviderOptions
{
    public const string SectionName = "Providers";

    public string? ApiKey { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>Data retention terms, recorded so the sensitivity allow-list has a justification.</summary>
    public string? DataPolicy { get; set; }
}
