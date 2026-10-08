using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresRunBriefs(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    TimeProvider time) : IRunBriefs
{
    public async Task<IReadOnlyList<BriefCandidate>> PendingAsync(
        int maxAttempts, TimeSpan staleAfter, int take, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var staleBefore = time.GetUtcNow() - staleAfter;
        var runs = await db.Runs.AsNoTracking()
            .Where(r => r.ClosedAt != null || r.AbandonedAt != null)
            .Where(r => db.RunMessages.Any(m => m.RunId == r.RunId))
            .Where(r => !db.RunBriefs.Any(b => b.RunId == r.RunId
                && (b.Status == RunBriefStatus.Briefed
                    || b.Attempts >= maxAttempts
                    || (b.Status == RunBriefStatus.Briefing && b.ClaimedAt > staleBefore))))
            .OrderByDescending(r => r.AbandonedAt ?? r.ClosedAt).ThenByDescending(r => r.RunId)
            .Take(take)
            .ToListAsync(ct);
        return runs
            .Select(r => new BriefCandidate(
                r.RunId, r.Family, r.Mode, r.AbandonedAt is null ? RunStatus.Closed : RunStatus.Abandoned, r.Failure))
            .ToList();
    }

    public async Task<bool> ClaimAsync(string runId, int maxAttempts, TimeSpan staleAfter, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        var staleBefore = now - staleAfter;
        var briefing = nameof(RunBriefStatus.Briefing);
        var failed = nameof(RunBriefStatus.Failed);

        // One statement, so two briefers that race for a run cannot both win it.
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO engine.run_briefs (run_id, status, attempts, claimed_at, updated_at)
            VALUES ({runId}, {briefing}, 1, {now}, {now})
            ON CONFLICT (run_id) DO UPDATE
            SET status = {briefing}, attempts = run_briefs.attempts + 1, claimed_at = {now}, updated_at = {now}, error = NULL
            WHERE run_briefs.attempts < {maxAttempts}
              AND (run_briefs.status = {failed} OR (run_briefs.status = {briefing} AND run_briefs.claimed_at < {staleBefore}))
            """, ct);
        return claimed == 1;
    }

    public async Task CompleteAsync(string runId, RunBrief brief, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        var json = EngineJson.Serialize(brief);
        await db.RunBriefs
            .Where(b => b.RunId == runId && b.Status == RunBriefStatus.Briefing)
            .ExecuteUpdateAsync(set => set
                .SetProperty(b => b.Status, RunBriefStatus.Briefed)
                .SetProperty(b => b.Brief, json)
                .SetProperty(b => b.Error, (string?)null)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    public async Task FailAsync(string runId, string error, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        await db.RunBriefs
            .Where(b => b.RunId == runId && b.Status == RunBriefStatus.Briefing)
            .ExecuteUpdateAsync(set => set
                .SetProperty(b => b.Status, RunBriefStatus.Failed)
                .SetProperty(b => b.Error, error)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    public async Task<RunBriefing?> FindAsync(string runId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await FindAsync(db, runId, ct);
    }

    public async Task ResetAsync(string runId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await db.RunBriefs.Where(b => b.RunId == runId).ExecuteDeleteAsync(ct);
    }

    internal static async Task<RunBriefing?> FindAsync(WorkflowEngineDbContext db, string runId, CancellationToken ct)
    {
        var entry = await db.RunBriefs.AsNoTracking().SingleOrDefaultAsync(b => b.RunId == runId, ct);
        return entry is null
            ? null
            : new RunBriefing(
                entry.Status,
                entry.Attempts,
                entry.Brief is null ? null : EngineJson.Deserialize<RunBrief>(entry.Brief),
                entry.Error,
                entry.UpdatedAt);
    }
}
