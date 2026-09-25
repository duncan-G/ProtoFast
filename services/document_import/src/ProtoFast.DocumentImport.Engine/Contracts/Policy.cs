namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// One row per (bucket, stage). A row is a ladder: the executor at each populated tier, which tier
/// is primary, and which tier is under shadow evaluation. <see cref="Tier.Orchestrator"/> is always
/// populated; its seed executor is the stage-scoped agent loop.
/// </summary>
public sealed record PolicyRow(
    string Bucket,
    string StageId,
    IReadOnlyDictionary<Tier, ExecutorRef> Ladder,
    Tier Tier,                        // current primary
    Confidence Confidence,            // primary pass rate
    Tier? Shadow,                     // next tier under evaluation; always a populated ladder slot
    Confidence ShadowConfidence,      // shadow pass rate
    DateTimeOffset UpdatedAt)
{
    /// <summary>The row a stage gets before it has one: Ladder = { Orchestrator }, Tier = Orchestrator.</summary>
    public static PolicyRow Default(string bucket, string stageId, ExecutorRef orchestrator, DateTimeOffset at) =>
        new(
            bucket,
            stageId,
            new Dictionary<Tier, ExecutorRef> { [Tier.Orchestrator] = orchestrator },
            Tier.Orchestrator,
            Confidence.Prior,
            null,
            Confidence.Prior,
            at);
}

/// <summary>Beta posterior on pass rate, time-decayed towards the prior.</summary>
public readonly record struct Confidence(double Alpha, double Beta)
{
    public static readonly Confidence Prior = new(1, 1);
    public double Mean         => Alpha / (Alpha + Beta);
    public double Observations => Alpha + Beta - 2;                 // effective count after decay
    public Confidence Decay(double factor) => new(1 + (Alpha - 1) * factor, 1 + (Beta - 1) * factor);
    public Confidence Observe(bool pass, double weight) =>
        pass ? this with { Alpha = Alpha + weight } : this with { Beta = Beta + weight };
}

public interface IPolicyStore
{
    // One read per run. A stage with no row gets the default: Ladder = { Orchestrator }, Tier = Orchestrator.
    Task<IReadOnlyDictionary<string, PolicyRow>> SnapshotAsync(
        string bucket, IEnumerable<string> stageIds, CancellationToken ct);
    Task<PolicyRow> GetAsync(string bucket, string stageId, CancellationToken ct);
    Task PutAsync(PolicyRow row, CancellationToken ct);
}

public static class Ladders
{
    /// <summary>
    /// The nearest populated tier to the left. Escalation and demotion both use it; there is
    /// nothing left of <see cref="Tier.Orchestrator"/>.
    /// </summary>
    public static Tier Below(this IReadOnlyDictionary<Tier, ExecutorRef> ladder, Tier tier)
    {
        for (var t = tier - 1; t >= Tier.Orchestrator; t--)
        {
            if (ladder.ContainsKey(t))
            {
                return t;
            }
        }

        throw new InvalidOperationException($"No populated tier below {tier}.");
    }
}
