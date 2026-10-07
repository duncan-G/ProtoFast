using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresRunInspector(IDbContextFactory<WorkflowEngineDbContext> contexts) : IRunInspector
{
    public const int MaxPageSize = 100;

    public async Task<RunPage> ListAsync(RunListQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 0);

        await using var db = await contexts.CreateDbContextAsync(ct);
        var runs = db.Runs.AsNoTracking();
        if (query.Family is { } family)
        {
            var generationPrefix = DocumentFamilyKeys.ForGeneration(family, 1)[..^1];
            runs = runs.Where(r => r.Family == family || r.Family.StartsWith(generationPrefix));
        }

        if (query.Mode is { } mode)
        {
            runs = runs.Where(r => r.Mode == mode);
        }

        runs = query.Status switch
        {
            RunStatus.Open => runs.Where(r => r.ClosedAt == null && r.AbandonedAt == null),
            RunStatus.Closed => runs.Where(r => r.ClosedAt != null && r.AbandonedAt == null),
            RunStatus.Abandoned => runs.Where(r => r.AbandonedAt != null),
            _ => runs,
        };

        var total = await runs.CountAsync(ct);
        var entries = await runs
            .OrderByDescending(r => r.OpenedAt).ThenByDescending(r => r.RunId)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new RunPage(await HeadersAsync(db, entries, ct), total);
    }

    public async Task<RunDetail?> FindAsync(string runId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.RunId == runId, ct);
        if (run is null)
        {
            return null;
        }

        var header = (await HeadersAsync(db, [run], ct))[0];
        var stages = (await db.StageRecords.AsNoTracking()
                .Where(s => s.RunId == runId)
                .OrderBy(s => s.Sequence)
                .ToListAsync(ct))
            .Select(s => new RecordedStage(EngineJson.Deserialize<StageRecord>(s.Record), s.RecordedAt))
            .ToList();
        var decisions = await db.RunDecisions.AsNoTracking()
            .Where(d => d.RunId == runId)
            .OrderBy(d => d.Sequence)
            .Select(d => new RecordedDecision(new Decision(d.Key, d.Choice, d.Rationale, d.Confidence), d.RecordedAt))
            .ToListAsync(ct);
        var progress = await db.RunProgress.AsNoTracking()
            .Where(p => p.RunId == runId)
            .OrderBy(p => p.SourceId)
            .Select(p => new RunProgress(p.Phase, p.RunId, p.StageId, p.Message, p.ResultId, p.Cost))
            .FirstOrDefaultAsync(ct);
        var messages = await db.RunMessages.AsNoTracking().CountAsync(m => m.RunId == runId, ct);

        return new RunDetail(header, stages, decisions, progress, messages);
    }

    public async Task<TranscriptPage> TranscriptAsync(string runId, int fromSequence, int take, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var messages = db.RunMessages.AsNoTracking().Where(m => m.RunId == runId);
        var total = await messages.CountAsync(ct);
        var page = await messages
            .Where(m => m.Sequence >= fromSequence)
            .OrderBy(m => m.Sequence)
            .Take(Math.Clamp(take, 1, MaxPageSize))
            .Select(m => new TranscriptMessage(m.Sequence, m.Message, m.RecordedAt))
            .ToListAsync(ct);
        var prompts = fromSequence > 0
            ? []
            : await db.RunSystemPrompts.AsNoTracking()
                .Where(p => p.RunId == runId)
                .OrderBy(p => p.FromSequence)
                .Select(p => new RunSystemPrompt(p.FromSequence, p.Prompt, p.RecordedAt))
                .ToListAsync(ct);
        return new TranscriptPage(page, total, prompts);
    }

    private static async Task<IReadOnlyList<RunHeader>> HeadersAsync(
        WorkflowEngineDbContext db, IReadOnlyList<RunEntry> runs, CancellationToken ct)
    {
        var ids = runs.Select(r => r.RunId).ToList();
        var records = (await db.StageRecords.AsNoTracking()
                .Where(s => ids.Contains(s.RunId))
                .OrderBy(s => s.Sequence)
                .Select(s => new { s.RunId, s.Record })
                .ToListAsync(ct))
            .ToLookup(s => s.RunId, s => EngineJson.Deserialize<StageRecord>(s.Record));
        var sources = (await db.RunProgress.AsNoTracking()
                .Where(p => p.RunId != null && ids.Contains(p.RunId))
                .Select(p => new { p.RunId, p.SourceId })
                .ToListAsync(ct))
            .ToLookup(p => p.RunId!, p => p.SourceId);

        return runs.Select(r =>
        {
            var attempts = records[r.RunId].ToList();
            return new RunHeader(
                r.RunId,
                new DocumentSignature(r.Family, EngineJson.Deserialize<Dictionary<string, string>>(r.Facets)),
                r.Mode,
                r.OpenedAt,
                r.ClosedAt,
                r.AbandonedAt,
                r.Failure,
                r.TraceId is null ? null : new TraceRef(r.TraceId),
                attempts.Count,
                Passed(attempts),
                attempts.Sum(a => a.Result.Cost.Amount),
                sources[r.RunId].Order(StringComparer.Ordinal).FirstOrDefault());
        }).ToList();
    }

    // Shadows never leave a stage, so only the last real attempt at each counts.
    private static bool Passed(IReadOnlyList<StageRecord> attempts)
    {
        var real = attempts.Where(a => !a.IsShadow).ToList();
        return real.Count > 0 && real.GroupBy(a => a.StageId).All(stage => stage.Last().Passed);
    }
}
