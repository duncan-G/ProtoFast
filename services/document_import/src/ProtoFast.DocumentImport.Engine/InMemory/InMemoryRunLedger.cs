using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryRunLedger : IRunLedger
{
    private readonly ConcurrentDictionary<string, Run> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RunProgress> _progress = new(StringComparer.Ordinal);
    private long _closedSequence;

    public Task OpenAsync(string runId, DocumentSignature documentSignature, RunMode mode, CancellationToken ct)
    {
        if (!_runs.TryAdd(runId, new Run(documentSignature, mode)))
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

        Update(record.RunId, p => p with { Cost = p.Cost + record.Result.Cost.Amount });

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

    public Task AbandonAsync(string runId, string reason, CancellationToken ct)
    {
        var run = Find(runId);
        lock (run)
        {
            run.Failure ??= reason;
        }

        return Task.CompletedTask;
    }

    public Task<RunSummary> SummariseAsync(string runId, CancellationToken ct) =>
        Task.FromResult(Summarise(runId, Find(runId)));

    public Task<RunSummary?> FindOpenAsync(string runId, CancellationToken ct)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            return Task.FromResult<RunSummary?>(null);
        }

        lock (run)
        {
            return Task.FromResult(run.ClosedSequence is null && run.Failure is null ? Summarise(runId, run) : null);
        }
    }

    public Task<IReadOnlyList<RunSummary>> RecentAsync(string family, RunMode mode, int take, CancellationToken ct)
    {
        IReadOnlyList<RunSummary> recent = Closed(family, mode)
            .OrderByDescending(r => r.Value.ClosedSequence)
            .Take(take)
            .Select(r => Summarise(r.Key, r.Value))
            .ToList();
        return Task.FromResult(recent);
    }

    public Task<int> CountAsync(string family, RunMode mode, CancellationToken ct) =>
        Task.FromResult(Closed(family, mode).Count());

    public Task AppendTranscriptAsync(string runId, int sequence, string json, CancellationToken ct)
    {
        var run = Find(runId);
        lock (run)
        {
            run.Transcript.TryAdd(sequence, json);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> TranscriptAsync(string runId, CancellationToken ct)
    {
        var run = Find(runId);
        lock (run)
        {
            return Task.FromResult<IReadOnlyList<string>>(run.Transcript.Values.ToList());
        }
    }

    public Task ReportAsync(string sourceId, RunProgress progress, CancellationToken ct)
    {
        _progress.AddOrUpdate(
            sourceId, progress, (_, known) => progress with { RunId = progress.RunId ?? known.RunId, Cost = known.Cost });
        return Task.CompletedTask;
    }

    public Task BeginStageAsync(string runId, string stageId, CancellationToken ct)
    {
        Update(runId, p => p with { StageId = stageId });
        return Task.CompletedTask;
    }

    private void Update(string runId, Func<RunProgress, RunProgress> change)
    {
        foreach (var sourceId in _progress.Keys)
        {
            RunProgress known;
            do
            {
                if (!_progress.TryGetValue(sourceId, out known!) || known.RunId != runId)
                {
                    break;
                }
            }
            while (!_progress.TryUpdate(sourceId, change(known), known));
        }
    }

    public Task<IReadOnlyDictionary<string, RunProgress>> ProgressAsync(
        IReadOnlyCollection<string> sourceIds, CancellationToken ct)
    {
        IReadOnlyDictionary<string, RunProgress> found = sourceIds
            .Distinct(StringComparer.Ordinal)
            .Where(_progress.ContainsKey)
            .ToDictionary(id => id, id => _progress[id], StringComparer.Ordinal);
        return Task.FromResult(found);
    }

    private IEnumerable<KeyValuePair<string, Run>> Closed(string family, RunMode mode) =>
        _runs.Where(r => r.Value.ClosedSequence is not null && r.Value.Mode == mode && r.Value.DocumentSignature.Family == family);

    private Run Find(string runId) =>
        _runs.TryGetValue(runId, out var run) ? run : throw new KeyNotFoundException($"Run {runId} was never opened.");

    private static RunSummary Summarise(string runId, Run run)
    {
        lock (run)
        {
            return new RunSummary(runId, run.DocumentSignature, run.Mode, run.Stages.ToList(), run.Trace, run.Decisions.ToList());
        }
    }

    private sealed class Run(DocumentSignature documentSignature, RunMode mode)
    {
        public DocumentSignature DocumentSignature { get; } = documentSignature;
        public RunMode Mode { get; } = mode;
        public List<StageRecord> Stages { get; } = [];
        public List<Decision> Decisions { get; } = [];
        public SortedDictionary<int, string> Transcript { get; } = [];
        public TraceRef? Trace { get; set; }
        public long? ClosedSequence { get; set; }
        public string? Failure { get; set; }
    }
}
