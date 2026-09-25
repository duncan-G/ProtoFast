using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresPolicyStore(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    EngineOptions options,
    TimeProvider time) : IPolicyStore
{
    public async Task<IReadOnlyDictionary<string, PolicyRow>> SnapshotAsync(
        string family, IEnumerable<string> stageIds, CancellationToken ct)
    {
        var ids = stageIds.Distinct(StringComparer.Ordinal).ToList();
        await using var db = await contexts.CreateDbContextAsync(ct);
        var stored = await db.StagePolicies.AsNoTracking()
            .Where(p => p.Family == family && ids.Contains(p.StageId))
            .ToDictionaryAsync(p => p.StageId, StringComparer.Ordinal, ct);

        return ids.ToDictionary(
            id => id,
            id => stored.TryGetValue(id, out var entry) ? ToRow(entry) : Default(family, id),
            StringComparer.Ordinal);
    }

    public async Task<PolicyRow> GetAsync(string family, string stageId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entry = await db.StagePolicies.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Family == family && p.StageId == stageId, ct);
        return entry is null ? Default(family, stageId) : ToRow(entry);
    }

    public async Task PutAsync(PolicyRow row, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entry = await db.StagePolicies.FindAsync([row.Family, row.StageId], ct);
        if (entry is null)
        {
            entry = new StagePolicyEntry { Family = row.Family, StageId = row.StageId, Ladder = string.Empty };
            db.StagePolicies.Add(entry);
        }

        entry.Ladder = EngineJson.Serialize(row.Ladder);
        entry.Primary = row.Primary;
        entry.ConfidenceAlpha = row.Confidence.Alpha;
        entry.ConfidenceBeta = row.Confidence.Beta;
        entry.Shadow = row.Shadow;
        entry.ShadowAlpha = row.ShadowConfidence.Alpha;
        entry.ShadowBeta = row.ShadowConfidence.Beta;
        entry.UpdatedAt = row.UpdatedAt;
        await db.SaveChangesAsync(ct);
    }

    private PolicyRow Default(string family, string stageId) =>
        PolicyRow.Default(family, stageId, options.Orchestrator, time.GetUtcNow());

    private static PolicyRow ToRow(StagePolicyEntry entry) =>
        new(
            entry.Family,
            entry.StageId,
            EngineJson.Deserialize<Dictionary<Tier, ExecutorRef>>(entry.Ladder),
            entry.Primary,
            new Confidence(entry.ConfidenceAlpha, entry.ConfidenceBeta),
            entry.Shadow,
            new Confidence(entry.ShadowAlpha, entry.ShadowBeta),
            entry.UpdatedAt);
}
