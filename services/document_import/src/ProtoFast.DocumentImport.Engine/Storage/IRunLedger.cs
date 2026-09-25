namespace ProtoFast.DocumentImport.Engine;

public interface IRunLedger
{
    Task OpenAsync(string runId, Signature signature, RunMode mode, CancellationToken ct);
    Task RecordAsync(StageRecord record, CancellationToken ct);
    Task RecordAsync(string runId, Decision decision, CancellationToken ct);
    Task CloseAsync(string runId, TraceRef? trace, CancellationToken ct);
    Task<RunSummary> SummariseAsync(string runId, CancellationToken ct);

    // Closed runs only, newest first.
    Task<IReadOnlyList<RunSummary>> RecentAsync(string family, RunMode mode, int take, CancellationToken ct);
    Task<int> CountAsync(string family, RunMode mode, CancellationToken ct);
}
