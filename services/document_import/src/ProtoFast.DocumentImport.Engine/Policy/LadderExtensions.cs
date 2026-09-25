using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Engine.Policy;

public static class LadderExtensions
{
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
