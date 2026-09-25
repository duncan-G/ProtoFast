namespace ProtoFast.DocumentImport.Engine;

/// <summary>A stage's accepted record in a run is its last passing non-shadow attempt.</summary>
public sealed class WorkflowMiner(EngineOptions options, TimeProvider time) : IWorkflowMiner
{
    public Task<MinedWorkflow?> MineAsync(string family, IReadOnlyList<RunSummary> runs, CancellationToken ct) =>
        Task.FromResult(Mine(family, runs));

    private MinedWorkflow? Mine(string family, IReadOnlyList<RunSummary> runs)
    {
        runs = runs.Where(r => r.Mode == RunMode.Discovery && r.Signature.Family == family).ToList();
        if (runs.Count == 0)
        {
            return null;
        }

        // The epsilon stops 0.8 * 10 rounding up to 9.
        var required = Math.Max(1, (int)Math.Ceiling(options.Thresholds.MinSupport * runs.Count - 1e-9));

        var accepted = runs
            .Select(r => r.Stages
                .Where(s => s is { IsShadow: false, Passed: true })
                .GroupBy(s => s.StageId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal))
            .ToList();

        var stageIds = accepted
            .SelectMany(a => a.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(id => accepted.Count(a => a.ContainsKey(id)) >= required)
            .ToList();
        if (stageIds.Count == 0)
        {
            return null;
        }

        var edges = SupportedEdges(accepted, stageIds.ToHashSet(StringComparer.Ordinal), required);

        var stages = stageIds.Select(id =>
        {
            var records = accepted.Where(a => a.ContainsKey(id)).Select(a => a[id]).ToList();
            var verifiers = records
                .GroupBy(r => string.Join('\n', r.Stage.Verifiers))
                .OrderByDescending(g => g.Count())
                .First()
                .First()
                .Stage.Verifiers;

            return new StageDefinition(
                id,
                edges.Where(e => e.To == id).Select(e => e.From).ToList(),
                MostCommon(records.Select(r => r.Stage.Input)),
                MostCommon(records.Select(r => r.Stage.Output)),
                verifiers,
                records[^1].Stage.Budget);
        }).ToList();

        var workflow = new WorkflowDefinition(
            new WorkflowRef($"mined:{family}", 0),
            StageGraph.TopologicalOrder(stages)!);

        var now = time.GetUtcNow();
        var seeds = stageIds.Select(id => Seed(family, id, runs, now)).ToList();

        return new MinedWorkflow(workflow, seeds);
    }

    private static List<(string From, string To)> SupportedEdges(
        List<Dictionary<string, StageRecord>> accepted, HashSet<string> stages, int required)
    {
        var candidates = accepted
            .SelectMany(a => a.Values.SelectMany(r => r.Stage.DependsOn
                .Where(stages.Contains)
                .Distinct(StringComparer.Ordinal)
                .Select(dependency => (From: dependency, To: r.StageId))))
            .Where(e => stages.Contains(e.To))
            .GroupBy(e => e)
            .Where(g => g.Count() >= required)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.From, StringComparer.Ordinal)
            .ThenBy(g => g.Key.To, StringComparer.Ordinal)
            .Select(g => g.Key);

        var kept = new List<(string From, string To)>();
        foreach (var edge in candidates)
        {
            if (!ClosesCycle(kept, edge))
            {
                kept.Add(edge);
            }
        }

        return kept;
    }

    private static bool ClosesCycle(List<(string From, string To)> edges, (string From, string To) edge)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([edge.To]);
        while (pending.TryPop(out var node))
        {
            if (node == edge.From)
            {
                return true;
            }

            if (seen.Add(node))
            {
                foreach (var next in edges.Where(e => e.From == node))
                {
                    pending.Push(next.To);
                }
            }
        }

        return false;
    }

    private PolicyRow Seed(string family, string stageId, IReadOnlyList<RunSummary> runs, DateTimeOffset now)
    {
        var records = runs
            .SelectMany(r => r.Stages)
            .Where(s => s.StageId == stageId && !s.IsShadow)
            .ToList();

        var favourite = records
            .Where(r => r.Tier != Tier.Orchestrator)
            .GroupBy(r => (r.Executor, r.Tier))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Count(r => r.Passed))
            .ThenBy(g => g.Key.Executor.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var row = PolicyRow.Default(family, stageId, options.Orchestrator, now);
        if (favourite is null)
        {
            return row with { Confidence = TrackRecord(records.Where(r => r.Tier == Tier.Orchestrator)) };
        }

        var (executor, tier) = favourite.Key;
        return row with
        {
            Ladder = new Dictionary<Tier, ExecutorRef> { [Tier.Orchestrator] = options.Orchestrator, [tier] = executor },
            Primary = tier,
            Confidence = TrackRecord(favourite),
        };
    }

    private static Confidence TrackRecord(IEnumerable<StageRecord> records) =>
        records.Aggregate(
            Confidence.Prior,
            (c, r) => c.Observe(r.Passed, r.Degraded ? Outcome.DegradedWeight : 1));

    private static T MostCommon<T>(IEnumerable<T> values) =>
        values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
}
