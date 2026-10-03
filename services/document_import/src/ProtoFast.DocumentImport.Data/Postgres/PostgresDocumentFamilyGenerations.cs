using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresDocumentFamilyGenerations(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    TimeProvider time) : IDocumentFamilyGenerations
{
    public async Task<int> CurrentAsync(string family, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.DocumentFamilyGenerations.AsNoTracking()
            .Where(g => g.Family == family)
            .Select(g => g.Generation)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<int> ResetAsync(string family, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();

        // One statement, so concurrent resets each get their own generation. Materialised as a list
        // because an upsert cannot be composed into the subquery SingleAsync would wrap it in.
        var generation = await db.Database
            .SqlQuery<int>($"""
                INSERT INTO engine.document_family_generations (family, generation, reset_at)
                VALUES ({family}, 1, {now})
                ON CONFLICT (family) DO UPDATE
                SET generation = document_family_generations.generation + 1, reset_at = excluded.reset_at
                RETURNING generation AS "Value"
                """)
            .ToListAsync(ct);
        return generation.Single();
    }
}
