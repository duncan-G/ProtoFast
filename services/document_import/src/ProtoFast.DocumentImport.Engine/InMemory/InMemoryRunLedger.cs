using System.Collections.Concurrent;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryRunLedger : IRunLedger
{
    private readonly ConcurrentDictionary<string, Run> _runs = new(StringComparer.Ordinal);
    private long _closedSequence;

    public Task OpenAsync(string runId, Signature signature, RunMode mode, CancellationToken ct)
    {
        if (!_runs.TryAdd(runId, new Run(signature, mode)))
        {
            throw new InvalidOperationException($"Run {runId} is already open.");
        }

        return Task.CompletedTask;
    }

    public Task RecordAsync(StageRecord record, CancellationToken ct)
    {
        var run = Find(record.RunId);
        lock (run)
        {
            run.Stages.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task RecordAsync(string runId, Decision decision, CancellationToken ct)
    {
        var run = Find(runId);
        lock (run)
        {
            run.Decisions.Add(decision);
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync(string runId, TraceRef? trace, CancellationToken ct)
    {
        var run = Find(runId);
        lock (run)
        {
            run.Trace = trace;
            run.ClosedSequence ??= Interlocked.Increment(ref _closedSequence);
        }

        return Task.CompletedTask;
    }

    public Task<RunSummary> SummariseAsync(string runId, CancellationToken ct) =>
        Task.FromResult(Summarise(runId, Find(runId)));

    public Task<IReadOnlyList<RunSummary>> RecentAsync(string bucket, RunMode mode, int take, CancellationToken ct)
    {
        IReadOnlyList<RunSummary> recent = Closed(bucket, mode)
            .OrderByDescending(r => r.Value.ClosedSequence)
            .Take(take)
            .Select(r => Summarise(r.Key, r.Value))
            .ToList();
        return Task.FromResult(recent);
    }

    public Task<int> CountAsync(string bucket, RunMode mode, CancellationToken ct) =>
        Task.FromResult(Closed(bucket, mode).Count());

    private IEnumerable<KeyValuePair<string, Run>> Closed(string bucket, RunMode mode) =>
        _runs.Where(r => r.Value.ClosedSequence is not null && r.Value.Mode == mode && r.Value.Signature.Bucket == bucket);

    private Run Find(string runId) =>
        _runs.TryGetValue(runId, out var run) ? run : throw new KeyNotFoundException($"Run {runId} was never opened.");

    private static RunSummary Summarise(string runId, Run run)
    {
        lock (run)
        {
            return new RunSummary(runId, run.Signature, run.Mode, run.Stages.ToList(), run.Trace, run.Decisions.ToList());
        }
    }

    private sealed class Run(Signature signature, RunMode mode)
    {
        public Signature Signature { get; } = signature;
        public RunMode Mode { get; } = mode;
        public List<StageRecord> Stages { get; } = [];
        public List<Decision> Decisions { get; } = [];
        public TraceRef? Trace { get; set; }
        public long? ClosedSequence { get; set; }
    }
}
