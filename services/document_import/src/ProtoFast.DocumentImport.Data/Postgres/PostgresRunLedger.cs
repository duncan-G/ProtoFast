using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresRunLedger(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    TimeProvider time) : IRunLedger
{
    public async Task OpenAsync(string runId, Signature signature, RunMode mode, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.Runs.Add(new RunEntry
        {
            RunId = runId,
            Family = signature.Family,
            Facets = EngineJson.Serialize(signature.Facets),
            Mode = mode,
            OpenedAt = time.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (PostgresErrors.IsUniqueViolation(e))
        {
            throw new InvalidOperationException($"Run {runId} is already open.", e);
        }
    }

    public Task RecordAsync(StageRecord record, CancellationToken ct) =>
        AppendAsync(record.RunId, new StageRecordEntry
        {
            RunId = record.RunId,
            StageId = record.StageId,
            Record = EngineJson.Serialize(record),
            RecordedAt = time.GetUtcNow(),
        }, ct);

    public Task RecordAsync(string runId, Decision decision, CancellationToken ct) =>
        AppendAsync(runId, new RunDecisionEntry
        {
            RunId = runId,
            Key = decision.Key,
            Choice = decision.Choice,
            Rationale = decision.Rationale,
            Confidence = decision.Confidence,
            RecordedAt = time.GetUtcNow(),
        }, ct);

    public async Task CloseAsync(string runId, TraceRef? trace, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        var updated = await db.Runs
            .Where(r => r.RunId == runId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.TraceId, trace.HasValue ? trace.Value.Id : null)
                .SetProperty(r => r.ClosedAt, r => r.ClosedAt ?? now), ct);
        if (updated == 0)
        {
            throw new KeyNotFoundException($"Run {runId} was never opened.");
        }
    }

    public async Task<RunSummary> SummariseAsync(string runId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.RunId == runId, ct)
            ?? throw new KeyNotFoundException($"Run {runId} was never opened.");
        return (await SummariseAsync(db, [run], ct))[0];
    }

    public async Task<IReadOnlyList<RunSummary>> RecentAsync(string family, RunMode mode, int take, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var runs = await Closed(db, family, mode)
            .OrderByDescending(r => r.ClosedAt).ThenByDescending(r => r.RunId)
            .Take(take)
            .ToListAsync(ct);
        return await SummariseAsync(db, runs, ct);
    }

    public async Task<int> CountAsync(string family, RunMode mode, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await Closed(db, family, mode).CountAsync(ct);
    }

    private static IQueryable<RunEntry> Closed(WorkflowEngineDbContext db, string family, RunMode mode) =>
        db.Runs.AsNoTracking().Where(r => r.Family == family && r.Mode == mode && r.ClosedAt != null);

    private async Task AppendAsync<TEntry>(string runId, TEntry entry, CancellationToken ct) where TEntry : class
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (PostgresErrors.IsForeignKeyViolation(e))
        {
            throw new KeyNotFoundException($"Run {runId} was never opened.", e);
        }
    }

    private static async Task<IReadOnlyList<RunSummary>> SummariseAsync(
        WorkflowEngineDbContext db, IReadOnlyList<RunEntry> runs, CancellationToken ct)
    {
        var ids = runs.Select(r => r.RunId).ToList();
        var records = (await db.StageRecords.AsNoTracking()
                .Where(s => ids.Contains(s.RunId))
                .OrderBy(s => s.Sequence)
                .ToListAsync(ct))
            .ToLookup(s => s.RunId, s => EngineJson.Deserialize<StageRecord>(s.Record));
        var decisions = (await db.RunDecisions.AsNoTracking()
                .Where(d => ids.Contains(d.RunId))
                .OrderBy(d => d.Sequence)
                .ToListAsync(ct))
            .ToLookup(d => d.RunId, d => new Decision(d.Key, d.Choice, d.Rationale, d.Confidence));

        return runs.Select(r => new RunSummary(
                r.RunId,
                new Signature(r.Family, EngineJson.Deserialize<Dictionary<string, string>>(r.Facets)),
                r.Mode,
                records[r.RunId].ToList(),
                r.TraceId is null ? null : new TraceRef(r.TraceId),
                decisions[r.RunId].ToList()))
            .ToList();
    }
}
