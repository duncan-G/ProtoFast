using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresMinedWorkflowStore(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    TimeProvider time) : IMinedWorkflowStore
{
    public async Task PutAsync(string family, MinedWorkflow mined, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.MinedWorkflowDrafts.Add(new MinedWorkflowDraftEntry
        {
            WorkflowId = mined.Workflow.Ref.Id,
            WorkflowVersion = mined.Workflow.Ref.Version,
            Family = family,
            Mined = EngineJson.Serialize(mined),
            MinedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<(string Family, MinedWorkflow Mined)?> GetAsync(WorkflowRef reference, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entry = await db.MinedWorkflowDrafts.AsNoTracking()
            .SingleOrDefaultAsync(d => d.WorkflowId == reference.Id && d.WorkflowVersion == reference.Version, ct);
        return entry is null ? null : (entry.Family, EngineJson.Deserialize<MinedWorkflow>(entry.Mined));
    }
}
