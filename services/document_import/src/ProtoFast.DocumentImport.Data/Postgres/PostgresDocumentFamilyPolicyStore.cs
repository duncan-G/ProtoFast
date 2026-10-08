using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresDocumentFamilyPolicyStore(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    TimeProvider time) : IDocumentFamilyPolicyStore
{
    public async Task<DocumentFamilyPolicy> GetAsync(string family, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entry = await db.DocumentFamilyPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.Family == family, ct);
        if (entry is null)
        {
            return DocumentFamilyPolicy.Default(family, time.GetUtcNow());
        }

        return new DocumentFamilyPolicy(
            entry.Family,
            entry.Mode,
            entry.WorkflowId is { } id ? new WorkflowRef(id, entry.WorkflowVersion!.Value) : null,
            new Confidence(entry.ConfidenceAlpha, entry.ConfidenceBeta),
            entry.UpdatedAt);
    }

    public async Task PutAsync(DocumentFamilyPolicy policy, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entry = await db.DocumentFamilyPolicies.FindAsync([policy.Family], ct);
        if (entry is null)
        {
            entry = new DocumentFamilyPolicyEntry { Family = policy.Family };
            db.DocumentFamilyPolicies.Add(entry);
        }

        entry.Mode = policy.Mode;
        entry.WorkflowId = policy.Workflow?.Id;
        entry.WorkflowVersion = policy.Workflow?.Version;
        entry.ConfidenceAlpha = policy.Confidence.Alpha;
        entry.ConfidenceBeta = policy.Confidence.Beta;
        entry.UpdatedAt = policy.UpdatedAt;
        await db.SaveChangesAsync(ct);
    }
}
