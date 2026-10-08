using ProtoFast.DocumentImport.Engine.Briefing;

namespace ProtoFast.DocumentImport.Engine.Storage;

/// <summary>Read-only views over the ledger for the admin console; nothing here is on a run's path.</summary>
public interface IRunInspector
{
    // Newest first.
    Task<RunPage> ListAsync(RunListQuery query, CancellationToken ct);

    Task<RunDetail?> FindAsync(string runId, CancellationToken ct);

    // Messages from the sequence onwards; sequences start at 0.
    Task<TranscriptPage> TranscriptAsync(string runId, int fromSequence, int take, CancellationToken ct);

    // Every recorded step and the run's brief, if the briefer has reached it.
    Task<RunReview> ReviewAsync(string runId, CancellationToken ct);
}
