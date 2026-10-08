namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <summary>Briefs of finished runs, written after the run by the briefer; nothing here is on a run's path.</summary>
public interface IRunBriefs
{
    // Ended runs that journaled a conversation and have no brief, a failed one with attempts left, or a
    // claim older than staleAfter; most recently ended first.
    Task<IReadOnlyList<BriefCandidate>> PendingAsync(int maxAttempts, TimeSpan staleAfter, int take, CancellationToken ct);

    // False when another briefer holds the run or it has no attempts left.
    Task<bool> ClaimAsync(string runId, int maxAttempts, TimeSpan staleAfter, CancellationToken ct);

    Task CompleteAsync(string runId, RunBrief brief, CancellationToken ct);
    Task FailAsync(string runId, string error, CancellationToken ct);

    Task<RunBriefing?> FindAsync(string runId, CancellationToken ct);

    // Drops the brief, attempts included, so the briefer writes it again.
    Task ResetAsync(string runId, CancellationToken ct);
}
