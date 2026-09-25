namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Narrows a policy row to the executors the scheduler may actually route to, at the moment the
/// run's policy is frozen. The learning plane should never produce a row this changes; the gate
/// is what makes the invariants hold if it does.
///
/// <list type="bullet">
/// <item>An executor that is not <see cref="ExecutorSpec.Promoted"/> is not routable.</item>
/// <item>A stage with no verifiers cannot leave <see cref="Tier.Orchestrator"/>.</item>
/// <item>A stage with no deterministic verifier cannot reach <see cref="Tier.Codified"/>.</item>
/// </list>
///
/// A primary that is removed falls to the nearest populated tier to the left; a shadow that is
/// removed, or is no longer right of the primary, is dropped.
/// </summary>
public sealed class PolicyGate(IRegistry registry, VerifierCatalog verifiers, EngineOptions options)
{
    public async Task<PolicyRow> ClampAsync(PolicyRow row, StageDefinition stage, CancellationToken ct)
    {
        var ladder = new Dictionary<Tier, ExecutorRef>
        {
            [Tier.Orchestrator] = row.Ladder.TryGetValue(Tier.Orchestrator, out var orchestrator)
                ? orchestrator
                : options.Orchestrator,
        };

        if (stage.Verifiers.Count > 0)
        {
            var allowCodified = await verifiers.HasDeterministicAsync(row.Bucket, stage.Verifiers, ct);
            foreach (var (tier, executor) in row.Ladder)
            {
                if (tier == Tier.Orchestrator || (tier == Tier.Codified && !allowCodified))
                {
                    continue;
                }

                if (await IsPromotedAsync(executor, ct))
                {
                    ladder[tier] = executor;
                }
            }
        }

        var primary = ladder.ContainsKey(row.Tier) ? row.Tier : ladder.Below(row.Tier);
        var shadow = row.Shadow is { } s && s > primary && ladder.ContainsKey(s) ? s : (Tier?)null;

        return row with
        {
            Ladder = ladder,
            Tier = primary,
            Shadow = shadow,
            ShadowConfidence = shadow is null ? Confidence.Prior : row.ShadowConfidence,
        };
    }

    private async Task<bool> IsPromotedAsync(ExecutorRef executor, CancellationToken ct)
    {
        try
        {
            return (await registry.ResolveAsync(executor, ct)).Promoted;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }
}
