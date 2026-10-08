using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;
using ProtoFast.DocumentImport.Engine.Families;

namespace ProtoFast.DocumentImport.Data.Postgres;

public sealed class PostgresDocumentFamilyRegistry(IDbContextFactory<WorkflowEngineDbContext> contexts) : IDocumentFamilyRegistry
{
    public async Task<IReadOnlyList<DocumentFamilyInfo>> ListAsync(CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entries = await db.DocumentFamilies.AsNoTracking().OrderBy(f => f.Family).ToListAsync(ct);
        return entries.Select(ToInfo).ToList();
    }

    public async Task CreateAsync(DocumentFamilyInfo info, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.DocumentFamilies.Add(new DocumentFamilyEntry
        {
            Family = info.Family,
            DisplayName = info.DisplayName,
            Description = info.Description,
            CreatedBy = info.CreatedBy,
            CreatedAt = info.CreatedAt,
            UpdatedAt = info.UpdatedAt,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (PostgresErrors.IsUniqueViolation(e))
        {
            throw new InvalidOperationException($"Document family '{info.Family}' is already registered.", e);
        }
    }

    public async Task UpdateAsync(DocumentFamilyInfo info, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entry = await db.DocumentFamilies.FindAsync([info.Family], ct);
        if (entry is null)
        {
            entry = new DocumentFamilyEntry
            {
                Family = info.Family,
                DisplayName = info.DisplayName,
                Description = info.Description,
                CreatedBy = info.CreatedBy,
                CreatedAt = info.CreatedAt,
            };
            db.DocumentFamilies.Add(entry);
        }

        entry.DisplayName = info.DisplayName;
        entry.Description = info.Description;
        entry.UpdatedAt = info.UpdatedAt;
        await db.SaveChangesAsync(ct);
    }

    internal static DocumentFamilyInfo ToInfo(DocumentFamilyEntry entry) =>
        new(entry.Family, entry.DisplayName, entry.Description, entry.CreatedBy, entry.CreatedAt, entry.UpdatedAt);
}
