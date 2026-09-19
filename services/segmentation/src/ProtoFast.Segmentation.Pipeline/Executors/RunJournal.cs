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

    /// <summary>
    /// The upload row behind a run. Phase 0 needs it for the source key's extension and for the
    /// media type the converter dispatches on — both of which are validated table values recorded
    /// at <c>CreateUpload</c>, not anything re-derived from the caller's filename.
    /// </summary>
    public async Task<Upload?> LoadUploadAsync(string uploadId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();
        return await db.Uploads.AsNoTracking().FirstOrDefaultAsync(u => u.UploadId == uploadId, ct);
    }

    /// <summary>Records which model a phase was pinned to, so the rest of the run stays consistent.</summary>
    public Task PinModelAsync(string runId, PipelinePhase phase, string modelKey, CancellationToken ct) =>
        PinModelAsync(runId, phase.ToString(), modelKey, ct);

    /// <summary>
    /// The same, keyed by role rather than by phase. Phase 5 runs two roles when it orchestrates,
    /// and both have to pin independently: windows must stay consistent with each other, and a
    /// resumed run must not switch orchestrator mid-document (orchestrator plan §7).
    /// </summary>
    public Task PinModelAsync(string runId, AgentRole role, string modelKey, CancellationToken ct) =>
        PinModelAsync(runId, role.ToString(), modelKey, ct);

    /// <summary>
    /// Records the composition family phase 5 re-confirmed from paragraph-level evidence
    /// (scene plan §6, §8.5 step 5).
    ///
    /// <para>Phase 0's guess was made from converter metadata and line shapes, which is the most
    /// that is knowable before paragraphs exist. This one is made from dialogue ratio, speaker-label
    /// density, tense and person — and it is the fallback every later phase resolves against, so it
    /// belongs on the run row rather than only in the artifact.</para>
    /// </summary>
    public async Task RecordCompositionFamilyAsync(string runId, string family, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var run = await db.Runs.FirstOrDefaultAsync(r => r.RunId == runId, ct);
        if (run is null || string.Equals(run.CompositionFamily, family, StringComparison.Ordinal))
        {
            return;
        }

        run.CompositionFamily = family;
        run.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task PinModelAsync(string runId, string pinKey, string modelKey, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        var run = await db.Runs.FirstOrDefaultAsync(r => r.RunId == runId, ct);
        if (run is null || run.PinnedModels.ContainsKey(pinKey))
        {
            return;
        }

        // The dictionary is a jsonb column with a value comparer, so it has to be replaced rather
        // than mutated for EF to see the change.
        run.PinnedModels = new Dictionary<string, string>(run.PinnedModels) { [pinKey] = modelKey };
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
