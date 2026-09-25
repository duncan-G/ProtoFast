namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Drops rungs the scheduler must not route to: unpromoted executors, every delegate tier of a
/// stage without verifiers, and Codified for a stage without a deterministic verifier.
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
            var allowCodified = await verifiers.HasDeterministicAsync(row.Family, stage.Verifiers, ct);
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

        var primary = ladder.ContainsKey(row.Primary) ? row.Primary : ladder.Below(row.Primary);
        var shadow = row.Shadow is { } s && s > primary && ladder.ContainsKey(s) ? s : (Tier?)null;

        return row with
        {
            Ladder = ladder,
            Primary = primary,
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
