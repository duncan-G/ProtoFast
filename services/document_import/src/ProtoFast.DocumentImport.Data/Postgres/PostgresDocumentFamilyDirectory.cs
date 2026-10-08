using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Families;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Data.Postgres;

/// <summary>
/// A family exists wherever any store has a row for it or one of its generations, so the
/// directory is a union over those stores rather than a table of its own; only what is written
/// about a family (<see cref="PostgresDocumentFamilyRegistry"/>) has one.
/// </summary>
public sealed class PostgresDocumentFamilyDirectory(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    TimeProvider time) : IDocumentFamilyDirectory
{
    public async Task<IReadOnlyList<DocumentFamilySummary>> ListAsync(CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var infos = await db.DocumentFamilies.AsNoTracking().ToDictionaryAsync(f => f.Family, StringComparer.Ordinal, ct);
        var generations = await db.DocumentFamilyGenerations.AsNoTracking()
            .ToDictionaryAsync(g => g.Family, g => g.Generation, StringComparer.Ordinal, ct);
        var policies = await db.DocumentFamilyPolicies.AsNoTracking().ToDictionaryAsync(p => p.Family, StringComparer.Ordinal, ct);
        var runs = await RunCountsAsync(db.Runs.AsNoTracking(), ct);
        var skills = await CountByFamilyAsync(db.DocumentFamilySkills.Where(s => s.RemovedAt == null).Select(s => s.Family), ct);
        var executors = await CountByFamilyAsync(db.DocumentFamilyExecutors.Select(e => e.Family), ct);
        var verifiers = await CountByFamilyAsync(db.DocumentFamilyVerifiers.Select(v => v.Family), ct);
        var stagePolicies = await db.StagePolicies.AsNoTracking().Select(p => p.Family).Distinct().ToListAsync(ct);

        var names = infos.Keys
            .Concat(generations.Keys)
            .Concat(policies.Keys.Concat(runs.Keys).Concat(skills.Keys).Concat(executors.Keys).Concat(verifiers.Keys)
                .Concat(stagePolicies).Select(key => DocumentFamilyKeys.Split(key).Family))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        return names.Select(name =>
        {
            var generation = generations.GetValueOrDefault(name);
            var key = DocumentFamilyKeys.ForGeneration(name, generation);
            var policy = policies.TryGetValue(key, out var entry) ? ToPolicy(entry) : DocumentFamilyPolicy.Default(key, time.GetUtcNow());
            var familyRuns = runs.Where(r => DocumentFamilyKeys.Split(r.Key).Family == name).Select(r => r.Value).ToList();
            return new DocumentFamilySummary(
                name,
                infos.TryGetValue(name, out var info) ? PostgresDocumentFamilyRegistry.ToInfo(info) : null,
                generation,
                policy.Mode,
                policy.Workflow,
                familyRuns.Sum(r => r.Runs),
                familyRuns.Sum(r => r.OpenRuns),
                familyRuns.Max(r => r.LastRunAt),
                skills.GetValueOrDefault(key),
                executors.GetValueOrDefault(key),
                verifiers.GetValueOrDefault(key));
        }).ToList();
    }

    public async Task<DocumentFamilyDetail?> FindAsync(string family, int? generation, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var info = await db.DocumentFamilies.AsNoTracking().SingleOrDefaultAsync(f => f.Family == family, ct);
        var current = await db.DocumentFamilyGenerations.AsNoTracking().SingleOrDefaultAsync(g => g.Family == family, ct);
        var shown = generation ?? current?.Generation ?? 0;
        var key = DocumentFamilyKeys.ForGeneration(family, shown);

        var generationPrefix = DocumentFamilyKeys.ForGeneration(family, 1)[..^1];
        var runs = await RunCountsAsync(
            db.Runs.AsNoTracking().Where(r => r.Family == family || r.Family.StartsWith(generationPrefix)), ct);
        var policyEntry = await db.DocumentFamilyPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.Family == key, ct);
        var skills = await db.DocumentFamilySkills.AsNoTracking()
            .Where(s => s.Family == key)
            .OrderBy(s => s.AddedAt).ThenBy(s => s.SkillId).ThenBy(s => s.SkillVersion)
            .Select(s => new FamilySkill(new SkillRef(s.SkillId, s.SkillVersion), s.AddedAt, s.RemovedAt, s.RemovalReason))
            .ToListAsync(ct);
        var executors = await db.DocumentFamilyExecutors.AsNoTracking()
            .Where(e => e.Family == key)
            .OrderBy(e => e.AddedAt).ThenBy(e => e.ExecutorId).ThenBy(e => e.ExecutorVersion)
            .Select(e => new FamilyExecutor(new ExecutorRef(e.ExecutorId, e.ExecutorVersion), e.AddedAt))
            .ToListAsync(ct);
        var verifiers = await db.DocumentFamilyVerifiers.AsNoTracking()
            .Where(v => v.Family == key)
            .OrderBy(v => v.AddedAt).ThenBy(v => v.VerifierId)
            .Select(v => new FamilyVerifier(new VerifierSpec(v.VerifierId, v.StageId, v.Rubric), v.AddedAt))
            .ToListAsync(ct);
        var stagePolicies = (await db.StagePolicies.AsNoTracking()
                .Where(p => p.Family == key)
                .OrderBy(p => p.StageId)
                .ToListAsync(ct))
            .Select(PostgresPolicyStore.ToRow)
            .ToList();
        var drafts = await db.MinedWorkflowDrafts.AsNoTracking()
            .Where(d => d.Family == key)
            .OrderBy(d => d.MinedAt)
            .Select(d => new
            {
                d.WorkflowId,
                d.WorkflowVersion,
                d.Mined,
                d.MinedAt,
                Promoted = db.RegistryEntries.Any(e =>
                    e.Kind == RegistryEntryKind.Workflow && e.Id == d.WorkflowId && e.Version == d.WorkflowVersion && e.Promoted),
            })
            .ToListAsync(ct);

        var known = info is not null || current is not null || runs.Count > 0 || policyEntry is not null
                    || skills.Count > 0 || executors.Count > 0 || verifiers.Count > 0 || stagePolicies.Count > 0;
        if (!known)
        {
            return null;
        }

        return new DocumentFamilyDetail(
            family,
            info is null ? null : PostgresDocumentFamilyRegistry.ToInfo(info),
            current?.Generation ?? 0,
            current?.ResetAt,
            shown,
            policyEntry is null ? DocumentFamilyPolicy.Default(key, time.GetUtcNow()) : ToPolicy(policyEntry),
            skills,
            executors,
            verifiers,
            stagePolicies,
            drafts.Select(d => new MinedWorkflowDraft(
                    new WorkflowRef(d.WorkflowId, d.WorkflowVersion),
                    EngineJson.Deserialize<MinedWorkflow>(d.Mined).Workflow.Stages.Count,
                    d.MinedAt,
                    d.Promoted))
                .ToList(),
            runs.Select(r => (DocumentFamilyKeys.Split(r.Key).Generation, Counts: r.Value))
                .OrderBy(r => r.Generation)
                .Select(r => new GenerationRuns(r.Generation, r.Counts.Runs, r.Counts.OpenRuns, r.Counts.LastRunAt))
                .ToList());
    }

    // Keyed by the family key as runs store it, generation suffix included.
    private static async Task<Dictionary<string, (int Runs, int OpenRuns, DateTimeOffset? LastRunAt)>> RunCountsAsync(
        IQueryable<RunEntry> runs, CancellationToken ct) =>
        await runs
            .GroupBy(r => r.Family)
            .Select(g => new
            {
                Family = g.Key,
                Runs = g.Count(),
                Open = g.Count(r => r.ClosedAt == null && r.AbandonedAt == null),
                Last = g.Max(r => (DateTimeOffset?)r.OpenedAt),
            })
            .ToDictionaryAsync(g => g.Family, g => (g.Runs, g.Open, g.Last), StringComparer.Ordinal, ct);

    private static async Task<Dictionary<string, int>> CountByFamilyAsync(IQueryable<string> families, CancellationToken ct) =>
        await families
            .GroupBy(f => f)
            .Select(g => new { Family = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Family, g => g.Count, StringComparer.Ordinal, ct);

    private static DocumentFamilyPolicy ToPolicy(DocumentFamilyPolicyEntry entry) =>
        new(
            entry.Family,
            entry.Mode,
            entry.WorkflowId is { } id ? new WorkflowRef(id, entry.WorkflowVersion!.Value) : null,
            new Confidence(entry.ConfidenceAlpha, entry.ConfidenceBeta),
            entry.UpdatedAt);
}
