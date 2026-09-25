using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresDocumentFamilyCatalog(
    IDbContextFactory<WorkflowEngineDbContext> contexts,
    TimeProvider time) : IDocumentFamilyCatalog
{
    public async Task AddExecutorAsync(string family, ExecutorRef executor, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.DocumentFamilyExecutors.Add(new DocumentFamilyExecutor
        {
            Family = family,
            ExecutorId = executor.Id,
            ExecutorVersion = executor.Version,
            AddedAt = time.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (PostgresErrors.IsUniqueViolation(e))
        {
            // Already in the family.
        }
    }

    public async Task<IReadOnlyList<ExecutorRef>> ExecutorsAsync(string family, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.DocumentFamilyExecutors.AsNoTracking()
            .Where(e => e.Family == family)
            .OrderBy(e => e.AddedAt).ThenBy(e => e.ExecutorId).ThenBy(e => e.ExecutorVersion)
            .Select(e => new ExecutorRef(e.ExecutorId, e.ExecutorVersion))
            .ToListAsync(ct);
    }

    public async Task AddVerifierAsync(string family, VerifierSpec spec, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.DocumentFamilyVerifiers.Add(new DocumentFamilyVerifier
        {
            Family = family,
            VerifierId = spec.Id,
            StageId = spec.StageId,
            Rubric = spec.Rubric,
            AddedAt = time.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (PostgresErrors.IsUniqueViolation(e))
        {
            throw new InvalidOperationException($"Verifier '{spec.Id}' is already defined in document family '{family}'.", e);
        }
    }

    public async Task<IReadOnlyList<VerifierSpec>> VerifiersAsync(string family, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.DocumentFamilyVerifiers.AsNoTracking()
            .Where(v => v.Family == family)
            .OrderBy(v => v.AddedAt).ThenBy(v => v.VerifierId)
            .Select(v => new VerifierSpec(v.VerifierId, v.StageId, v.Rubric))
            .ToListAsync(ct);
    }
}
