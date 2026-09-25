namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Turns a bucket's discovery ledgers into a workflow draft. Per run, a stage's accepted record is
/// its last passing non-shadow attempt: that is the output the agent went on with.
///
/// <list type="bullet">
/// <item>A stage enters the DAG when at least MinSupport of the runs accepted an output for it.</item>
/// <item>An edge enters when at least MinSupport of the runs recorded it on the accepted record,
/// strongest first, skipping any edge that would close a cycle across runs.</item>
/// <item>Contracts and verifier sets are the ones most runs used; the budget is the latest.</item>
/// <item>The executor the agent delegated to most becomes the primary, seeded with its discovery
/// track record. A stage the agent always did itself stays at Orchestrator with the agent's.</item>
/// </list>
/// </summary>
public sealed class WorkflowMiner(EngineOptions options, TimeProvider time) : IWorkflowMiner
{
    public Task<MinedWorkflow?> MineAsync(string bucket, IReadOnlyList<RunSummary> runs, CancellationToken ct) =>
        Task.FromResult(Mine(bucket, runs));

    private MinedWorkflow? Mine(string bucket, IReadOnlyList<RunSummary> runs)
    {
        runs = runs.Where(r => r.Mode == RunMode.Discovery && r.Signature.Bucket == bucket).ToList();
        if (runs.Count == 0)
        {
            return null;
        }

        // Rounded up to a whole run count, tolerating 0.8 * 10 landing a hair above 8.
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
            new WorkflowRef($"mined:{bucket}", 0),
            Dag.TopologicalOrder(stages)!);

        var now = time.GetUtcNow();
        var seeds = stageIds.Select(id => Seed(bucket, id, runs, now)).ToList();

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
            if (!Reaches(kept, edge.To, edge.From))
            {
                kept.Add(edge);
            }
        }

        return kept;
    }

    // Whether `to` is reachable from `from` over the kept edges; adding from->to would then be a cycle.
    private static bool Reaches(List<(string From, string To)> edges, string from, string to)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([from]);
        while (pending.TryPop(out var node))
        {
            if (node == to)
            {
                return true;
            }

            if (seen.Add(node))
            {
                foreach (var edge in edges.Where(e => e.From == node))
                {
                    pending.Push(edge.To);
                }
            }
        }

        return false;
    }

    private PolicyRow Seed(string bucket, string stageId, IReadOnlyList<RunSummary> runs, DateTimeOffset now)
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

        var row = PolicyRow.Default(bucket, stageId, options.Orchestrator, now);
        if (favourite is null)
        {
            return row with { Confidence = TrackRecord(records.Where(r => r.Tier == Tier.Orchestrator)) };
        }

        var (executor, tier) = favourite.Key;
        return row with
        {
            Ladder = new Dictionary<Tier, ExecutorRef> { [Tier.Orchestrator] = options.Orchestrator, [tier] = executor },
            Tier = tier,
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
