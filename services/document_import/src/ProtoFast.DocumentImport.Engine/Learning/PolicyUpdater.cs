namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Moves confidence and tiers off the hot path. Runs as the single writer for its bus partition.
///
/// <para>Only the primary and the shadow executor move a row; escalation fallbacks and retired
/// executors earn nothing. Promotion carries the shadow's track record into the primary, which is
/// what makes demotion safe: a freshly promoted tier holds at least MinObservations, so one bad run
/// cannot drop it below DemoteAt, but a real regression will within a handful.</para>
/// </summary>
public sealed class PolicyUpdater(
    IPolicyStore store,
    IBucketPolicyStore buckets,
    IDistiller distiller,
    EngineOptions options) : IPolicyUpdater
{
    private Thresholds T => options.Thresholds;

    public Task ApplyAsync(Outcome outcome, CancellationToken ct) =>
        outcome.StageId is null ? ApplyToBucketAsync(outcome, ct) : ApplyToStageAsync(outcome, outcome.StageId, ct);

    private async Task ApplyToStageAsync(Outcome o, string stageId, CancellationToken ct)
    {
        var row = await store.GetAsync(o.Bucket, stageId, ct);
        var decay = Decay(row.UpdatedAt, o.At);

        if (o.Executor == row.Ladder[row.Tier])
        {
            row = row with { Confidence = row.Confidence.Decay(decay).Observe(o.IsPass, o.Weight) };
        }
        else if (row.Shadow is { } shadow && o.Executor == row.Ladder[shadow])
        {
            row = row with { ShadowConfidence = row.ShadowConfidence.Decay(decay).Observe(o.IsPass, o.Weight) };
        }
        else
        {
            return;                                   // escalation fallbacks and retired executors do not move policy
        }

        row = row switch
        {
            // Promote: the shadow becomes primary and keeps its track record as the new confidence.
            { Shadow: { } s } when T.Ready(row.ShadowConfidence)
                => row with { Tier = s, Confidence = row.ShadowConfidence, Shadow = null, ShadowConfidence = Confidence.Prior },

            // Demote: step to the nearest populated tier left; the old primary goes back to shadow.
            { Tier: > Tier.Orchestrator } when T.ShouldDemote(row.Confidence)
                => row with
                {
                    Tier = row.Ladder.Below(row.Tier), Confidence = Confidence.Prior,
                    Shadow = row.Tier, ShadowConfidence = Confidence.Prior,
                },

            _ => row,
        };

        row = row with { UpdatedAt = Latest(row.UpdatedAt, o.At) };
        await store.PutAsync(row, ct);

        // Earned a shadow but has none: ask the distiller for a next-tier candidate. Off the row's
        // write path; the candidate arrives later as its own PutAsync when it is Promoted.
        if (row is { Shadow: null, Tier: < Tier.Codified } && T.Ready(row.Confidence))
        {
            await distiller.RequestCandidateAsync(row, ct);
        }
    }

    /// <summary>
    /// Bucket-level outcomes judge a scheduled run's terminal output. While the bucket is in
    /// discovery they come from sampled shadow runs of the promoted workflow and, on reaching
    /// PromoteAt over MinObservations, flip it to scheduled. In scheduled mode they come from every
    /// run, and falling below DemoteAt flips it back to discovery with the workflow cleared, so
    /// mining starts over on the new ledgers.
    /// </summary>
    private async Task ApplyToBucketAsync(Outcome o, CancellationToken ct)
    {
        var policy = await buckets.GetAsync(o.Bucket, ct);
        if (policy.Workflow is not { } workflow || o.Executor != workflow.AsExecutor())
        {
            return;                                   // a retired workflow's outcome
        }

        policy = policy with
        {
            Confidence = policy.Confidence.Decay(Decay(policy.UpdatedAt, o.At)).Observe(o.IsPass, o.Weight),
        };

        policy = policy switch
        {
            { Mode: RunMode.Discovery } when T.Ready(policy.Confidence)
                => policy with { Mode = RunMode.Scheduled },

            { Mode: RunMode.Scheduled } when T.ShouldDemote(policy.Confidence)
                => policy with { Mode = RunMode.Discovery, Workflow = null, Confidence = Confidence.Prior },

            _ => policy,
        };

        await buckets.PutAsync(policy with { UpdatedAt = Latest(policy.UpdatedAt, o.At) }, ct);
    }

    // Outcomes can arrive out of order; an older one is observed without decay rather than
    // "un-decaying" the posterior.
    private double Decay(DateTimeOffset updatedAt, DateTimeOffset at) =>
        Math.Pow(0.5, Math.Max(0, (at - updatedAt).TotalDays) / T.HalfLifeDays);

    private static DateTimeOffset Latest(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
