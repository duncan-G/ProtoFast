using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Entities;

namespace ProtoFast.DocumentImport.Data;

/// <summary>
/// The engine's slice of the <c>protofast</c> database. Holds only what changes or is queried:
/// version numbers, promotion and document family membership. Registry content itself is
/// frozen in the object store.
/// </summary>
public sealed class WorkflowEngineDbContext(DbContextOptions<WorkflowEngineDbContext> options) : DbContext(options)
{
    public const string Schema = "engine";

    private const int IdLength = 200;
    private const int HashLength = 64;

    public DbSet<RegistryEntry> RegistryEntries => Set<RegistryEntry>();

    public DbSet<DocumentFamilyExecutor> DocumentFamilyExecutors => Set<DocumentFamilyExecutor>();

    public DbSet<DocumentFamilyVerifier> DocumentFamilyVerifiers => Set<DocumentFamilyVerifier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<RegistryEntry>(entity =>
        {
            // The key is what makes version assignment safe: two publishers that pick the same
            // next version cannot both insert it.
            entity.HasKey(e => new { e.Kind, e.Id, e.Version });
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.Id).HasMaxLength(IdLength);
            entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(HashLength);
        });

        modelBuilder.Entity<DocumentFamilyExecutor>(entity =>
        {
            entity.HasKey(e => new { e.Family, e.ExecutorId, e.ExecutorVersion });
            entity.Property(e => e.Family).HasMaxLength(IdLength);
            entity.Property(e => e.ExecutorId).HasMaxLength(IdLength);
        });

        modelBuilder.Entity<DocumentFamilyVerifier>(entity =>
        {
            entity.HasKey(e => new { e.Family, e.VerifierId });
            entity.Property(e => e.Family).HasMaxLength(IdLength);
            entity.Property(e => e.VerifierId).HasMaxLength(IdLength);
            entity.Property(e => e.StageId).IsRequired().HasMaxLength(IdLength);
            entity.Property(e => e.Rubric).IsRequired();
        });
    }
}
