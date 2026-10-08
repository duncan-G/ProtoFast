using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Engine.Learning;

public sealed class PolicyUpdater(
    IPolicyStore store,
    IDocumentFamilyPolicyStore families,
    IDistiller distiller,
    EngineOptions options) : IPolicyUpdater
{
    private Thresholds T => options.Thresholds;

    public Task ApplyAsync(Outcome outcome, CancellationToken ct) =>
        outcome.StageId is null ? ApplyToFamilyAsync(outcome, ct) : ApplyToStageAsync(outcome, outcome.StageId, ct);

    private async Task ApplyToStageAsync(Outcome o, string stageId, CancellationToken ct)
    {
        var row = await store.GetAsync(o.Family, stageId, ct);
        var decay = Decay(row.UpdatedAt, o.At);

        if (o.Executor == row.Ladder[row.Primary])
        {
            row = row with { Confidence = row.Confidence.Decay(decay).Observe(o.IsPass, o.Weight) };
        }
        else if (row.Shadow is { } shadow && o.Executor == row.Ladder[shadow])
        {
            row = row with { ShadowConfidence = row.ShadowConfidence.Decay(decay).Observe(o.IsPass, o.Weight) };
        }
        else
        {
            return;                                   // an escalation fallback or retired executor
        }

        row = row switch
        {
            // Promote, carrying the shadow's track record.
            { Shadow: { } s } when T.Ready(row.ShadowConfidence)
                => row with { Primary = s, Confidence = row.ShadowConfidence, Shadow = null, ShadowConfidence = Confidence.Prior },

            // Demote; the old primary goes back to shadow.
            { Primary: > Tier.Orchestrator } when T.ShouldDemote(row.Confidence)
                => row with
                {
                    Primary = row.Ladder.Below(row.Primary), Confidence = Confidence.Prior,
                    Shadow = row.Primary, ShadowConfidence = Confidence.Prior,
                },

            _ => row,
        };

        row = row with { UpdatedAt = Latest(row.UpdatedAt, o.At) };
        await store.PutAsync(row, ct);

        // Ready with no shadow: ask for the next tier's candidate.
        if (row is { Shadow: null, Primary: < Tier.Codified } && T.Ready(row.Confidence))
        {
            await distiller.RequestCandidateAsync(row, ct);
        }
    }

    private async Task ApplyToFamilyAsync(Outcome o, CancellationToken ct)
    {
        var policy = await families.GetAsync(o.Family, ct);
        if (policy.Workflow is not { } workflow || o.Executor != workflow.AsExecutor())
        {
            return;                                   // a retired workflow
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

        await families.PutAsync(policy with { UpdatedAt = Latest(policy.UpdatedAt, o.At) }, ct);
    }

    // Clamped so an out-of-order outcome cannot inflate the posterior.
    private double Decay(DateTimeOffset updatedAt, DateTimeOffset at) =>
        Math.Pow(0.5, Math.Max(0, (at - updatedAt).TotalDays) / T.HalfLifeDays);

    private static DateTimeOffset Latest(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
