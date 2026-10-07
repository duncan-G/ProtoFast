using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Storage;

public interface IRunLedger
{
    Task OpenAsync(string runId, DocumentSignature documentSignature, RunMode mode, CancellationToken ct);
    Task RecordAsync(StageRecord record, CancellationToken ct);
    Task RecordAsync(string runId, Decision decision, CancellationToken ct);
    Task CloseAsync(string runId, TraceRef? trace, CancellationToken ct);

    // A run that failed on its own terms; it is never closed, so never mined or resumed.
    Task AbandonAsync(string runId, string reason, CancellationToken ct);

    Task<RunSummary> SummariseAsync(string runId, CancellationToken ct);

    // Null unless the run exists and is neither closed nor abandoned.
    Task<RunSummary?> FindOpenAsync(string runId, CancellationToken ct);

    // Closed runs only, newest first.
    Task<IReadOnlyList<RunSummary>> RecentAsync(string family, RunMode mode, int take, CancellationToken ct);
    Task<int> CountAsync(string family, RunMode mode, CancellationToken ct);

    // The run's conversation, one JSON document per message, kept so an interrupted run resumes
    // where it stopped. Appending a sequence the run already has is a no-op.
    Task AppendTranscriptAsync(string runId, int sequence, string json, CancellationToken ct);
    Task<IReadOnlyList<string>> TranscriptAsync(string runId, CancellationToken ct);

    // The system prompt the conversation ran under. A resumed run can list skills the first attempt
    // wrote, so each prompt is kept from the message it took effect at; repeating the latest is a no-op.
    Task RecordSystemPromptAsync(string runId, int fromSequence, string prompt, CancellationToken ct);

    // Oldest first.
    Task<IReadOnlyList<RunSystemPrompt>> SystemPromptsAsync(string runId, CancellationToken ct);

    // Progress is keyed by source: the id the run's input is stored under, so one entry follows
    // every attempt at a source. Each report replaces the last, except that a report naming no
    // run keeps the last one named, and only the first report sets the cost.
    Task ReportAsync(string sourceId, RunProgress progress, CancellationToken ct);

    // A no-op unless a source's progress names this run, so shadow runs never move it.
    Task BeginStageAsync(string runId, string stageId, CancellationToken ct);

    // Sources with nothing reported are left out.
    Task<IReadOnlyDictionary<string, RunProgress>> ProgressAsync(IReadOnlyCollection<string> sourceIds, CancellationToken ct);
}
