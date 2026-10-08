using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Briefing;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

/// <summary>Offers its candidates until each is briefed, and claims whatever is asked for.</summary>
internal sealed class RecordingRunBriefs(params BriefCandidate[] candidates) : IRunBriefs
{
    public ConcurrentDictionary<string, RunBrief> Completed { get; } = new();

    public ConcurrentQueue<(string RunId, string Error)> Failed { get; } = new();

    public Task<IReadOnlyList<BriefCandidate>> PendingAsync(int maxAttempts, TimeSpan staleAfter, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BriefCandidate>>(candidates.Where(c => !Completed.ContainsKey(c.RunId)).Take(take).ToList());

    public Task<bool> ClaimAsync(string runId, int maxAttempts, TimeSpan staleAfter, CancellationToken ct) => Task.FromResult(true);

    public Task CompleteAsync(string runId, RunBrief brief, CancellationToken ct)
    {
        Completed[runId] = brief;
        return Task.CompletedTask;
    }

    public Task FailAsync(string runId, string error, CancellationToken ct)
    {
        Failed.Enqueue((runId, error));
        return Task.CompletedTask;
    }

    public Task<RunBriefing?> FindAsync(string runId, CancellationToken ct) => Task.FromResult<RunBriefing?>(null);

    public Task ResetAsync(string runId, CancellationToken ct) => Task.CompletedTask;
}
