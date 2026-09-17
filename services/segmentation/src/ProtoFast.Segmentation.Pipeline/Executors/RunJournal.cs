using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Data.Entities;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Records phase transitions in Postgres.
///
/// <para>This is what ThePlot's progress view reads, so it is written as the run happens rather
/// than at the end. It is also where the repair-round counters live (plan §13.2): keeping them in
/// the database rather than in worker memory is what stops a crash-loop from getting a fresh
/// budget of provider calls on every resume.</para>
/// </summary>
public sealed class RunJournal(IServiceScopeFactory scopes)
{
    public async Task StartAsync(string runId, PipelinePhase phase, string idempotencyKey, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var row = await db.RunPhases.FirstOrDefaultAsync(p => p.RunId == runId && p.Phase == phase, ct);
        if (row is null)
        {
            row = new RunPhase { RunId = runId, Phase = phase };
            db.RunPhases.Add(row);
        }

        row.State = PhaseState.Running;
        row.StartedAt = DateTimeOffset.UtcNow;
        row.IdempotencyKey = idempotencyKey;
        row.Error = null;

        db.RunEvents.Add(Event(runId, phase, PhaseState.Running, "started"));
        await SaveAsync(db, runId, ct);
    }

    public async Task CompleteAsync(
        string runId, PipelinePhase phase, string? artifactKey, string message, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var row = await db.RunPhases.FirstOrDefaultAsync(p => p.RunId == runId && p.Phase == phase, ct);
        if (row is not null)
        {
            row.State = PhaseState.Done;
            row.FinishedAt = DateTimeOffset.UtcNow;
            row.ArtifactKey = artifactKey;
        }

        db.RunEvents.Add(Event(runId, phase, PhaseState.Done, message));
        await SaveAsync(db, runId, ct);
    }

    public async Task SkipAsync(string runId, PipelinePhase phase, string reason, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var row = await db.RunPhases.FirstOrDefaultAsync(p => p.RunId == runId && p.Phase == phase, ct);
        if (row is null)
        {
            row = new RunPhase { RunId = runId, Phase = phase };
            db.RunPhases.Add(row);
        }

        row.State = PhaseState.Skipped;
        row.FinishedAt = DateTimeOffset.UtcNow;

        db.RunEvents.Add(Event(runId, phase, PhaseState.Skipped, reason));
        await SaveAsync(db, runId, ct);
    }

    public async Task FailAsync(string runId, PipelinePhase phase, string error, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var row = await db.RunPhases.FirstOrDefaultAsync(p => p.RunId == runId && p.Phase == phase, ct);
        if (row is not null)
        {
            row.State = PhaseState.Failed;
            row.FinishedAt = DateTimeOffset.UtcNow;
            row.Error = Truncate(error, 4000);
        }

        var run = await db.Runs.FirstOrDefaultAsync(r => r.RunId == runId, ct);
        if (run is not null)
        {
            run.Error = Truncate(error, 4000);
        }

        db.RunEvents.Add(Event(runId, phase, PhaseState.Failed, Truncate(error, 1024)));
        await SaveAsync(db, runId, ct);
    }

    /// <summary>A free-text progress note against a phase that is still running.</summary>
    public async Task NoteAsync(string runId, PipelinePhase phase, string message, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        db.RunEvents.Add(Event(runId, phase, PhaseState.Running, Truncate(message, 1024)));
        await SaveAsync(db, runId, ct);
    }

    /// <summary>
    /// Increments and returns this phase's repair-round count. Atomic, because two workers could
    /// briefly overlap on the same run after a visibility expiry and both must see the true count.
    /// </summary>
    public async Task<int> NextRepairRoundAsync(string runId, PipelinePhase phase, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        await db.RunPhases
            .Where(p => p.RunId == runId && p.Phase == phase)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RepairRounds, p => p.RepairRounds + 1), ct);

        return await db.RunPhases
            .Where(p => p.RunId == runId && p.Phase == phase)
            .Select(p => p.RepairRounds)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>The run row, or null when the run has been deleted out from under the worker.</summary>
    public async Task<Run?> LoadRunAsync(string runId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();
        return await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.RunId == runId, ct);
    }

    /// <summary>Records which model a phase was pinned to, so the rest of the run stays consistent.</summary>
    public async Task PinModelAsync(string runId, PipelinePhase phase, string modelKey, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var run = await db.Runs.FirstOrDefaultAsync(r => r.RunId == runId, ct);
        if (run is null || run.PinnedModels.ContainsKey(phase.ToString()))
        {
            return;
        }

        // The dictionary is a jsonb column with a value comparer, so it has to be replaced rather
        // than mutated for EF to see the change.
        run.PinnedModels = new Dictionary<string, string>(run.PinnedModels) { [phase.ToString()] = modelKey };
        run.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Persists the journal rows and stamps the run's <c>UpdatedAt</c>.
    ///
    /// <para>Loaded and mutated rather than <c>ExecuteUpdate</c>: this is one row, it is already
    /// in the change tracker on most paths, and a set-based update would run as a second statement
    /// outside the same <c>SaveChanges</c> — so a failure between the two would leave the events
    /// written and the run's timestamp stale. The counters below, which concurrent callers really
    /// do race on, keep their atomic form.</para>
    /// </summary>
    private static async Task SaveAsync(SegmentationDbContext db, string runId, CancellationToken ct)
    {
        var run = await db.Runs.FirstOrDefaultAsync(r => r.RunId == runId, ct);
        if (run is not null)
        {
            run.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private static RunEvent Event(string runId, PipelinePhase phase, PhaseState state, string message) =>
        new()
        {
            RunId = runId,
            Phase = phase,
            State = state,
            Message = message,
            At = DateTimeOffset.UtcNow,
        };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
