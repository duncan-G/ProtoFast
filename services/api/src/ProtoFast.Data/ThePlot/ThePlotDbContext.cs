using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot;

/// <summary>
/// ThePlot's slice of the <c>protofast</c> database: every table maps into the <c>plot</c>
/// schema. Builds on the shared <see cref="DbContextBase"/> so the unit-of-work, repository and
/// user-scoping machinery under <c>services/shared/Database</c> apply.
/// </summary>
public sealed class ThePlotDbContext(
    DbContextOptions<ThePlotDbContext> options,
    QueryFilterService queryFilterService,
    UserContext userContext)
    : DbContextBase(options, queryFilterService, userContext)
{
    public const string Schema = "plot";

    /// <summary>The length of an upload id: a 26-character Crockford ULID.</summary>
    private const int UploadIdLength = 26;

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentUpload> DocumentUploads => Set<DocumentUpload>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DocumentUpload>(entity =>
        {
            entity.HasKey(u => u.UploadId);
            entity.Property(u => u.UploadId).IsRequired().HasMaxLength(UploadIdLength).ValueGeneratedNever();
            entity.Property(u => u.UserId).IsRequired().HasMaxLength(255);
            entity.Property(u => u.FileName).IsRequired().HasMaxLength(255);
            entity.Property(u => u.MediaType).IsRequired().HasMaxLength(255);
            entity.Property(u => u.FileExtension).IsRequired().HasMaxLength(16);

            entity.HasIndex(u => u.UserId);
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).IsRequired().HasMaxLength(UploadIdLength).ValueGeneratedNever();
            entity.Property(d => d.UserId).IsRequired().HasMaxLength(255);
            entity.Property(d => d.Name).IsRequired().HasMaxLength(255);
            entity.Property(d => d.FileName).IsRequired().HasMaxLength(255);
            entity.Property(d => d.MediaType).IsRequired().HasMaxLength(255);
            entity.Property(d => d.FileExtension).IsRequired().HasMaxLength(16);
            entity.Property(d => d.StorageKey).IsRequired().HasMaxLength(512);

            // A document is the upload that landed: its id is the upload's, and the upload row has
            // to exist first. No navigation on either side — the two are read independently and
            // the document carries its own owner, so the query filter needs no path through here.
            entity.HasOne<DocumentUpload>()
                .WithOne()
                .HasForeignKey<Document>(d => d.Id)
                .OnDelete(DeleteBehavior.Cascade);

            // The desk lists a user's documents newest first.
            entity.HasIndex(d => new { d.UserId, d.DateCreated });
        });
    }
}
