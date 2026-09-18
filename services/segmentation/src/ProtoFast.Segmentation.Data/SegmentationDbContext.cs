using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Segmentation.Data.Entities;

namespace ProtoFast.Segmentation.Data;

/// <summary>
/// The <c>segmentation</c> database context. A plain <see cref="DbContext"/> with a
/// <see cref="DbContextOptions{TContext}"/> constructor, mirroring <c>AuthDbContext</c>, so EF
/// tooling, the schema-migrations runner and the two services all resolve it identically. Like
/// auth's, it deliberately avoids the shared <c>AddCoreDatabaseServices</c> helper, which
/// hard-wires pgvector that this Postgres does not have.
/// </summary>
public sealed class SegmentationDbContext(DbContextOptions<SegmentationDbContext> options) : DbContext(options)
{
    public DbSet<Run> Runs => Set<Run>();

    public DbSet<RunPhase> RunPhases => Set<RunPhase>();

    public DbSet<RunEvent> RunEvents => Set<RunEvent>();

    public DbSet<ReviewTask> ReviewTasks => Set<ReviewTask>();

    public DbSet<ModelCall> ModelCalls => Set<ModelCall>();

    public DbSet<Qualification> Qualifications => Set<Qualification>();

    public DbSet<FamilyInstinct> FamilyInstincts => Set<FamilyInstinct>();

    public DbSet<RunResult> RunResults => Set<RunResult>();

    public DbSet<Upload> Uploads => Set<Upload>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Run>(entity =>
        {
            entity.HasKey(r => r.RunId);
            entity.Property(r => r.RunId).HasMaxLength(32);
            entity.Property(r => r.OwnerSubject).IsRequired().HasMaxLength(255);
            entity.Property(r => r.DocumentId).IsRequired().HasMaxLength(255);
            entity.Property(r => r.DocumentFamily).IsRequired().HasMaxLength(64);
            entity.Property(r => r.UploadId).IsRequired().HasMaxLength(64);
            entity.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
            entity.Property(r => r.ReviewState).IsRequired().HasMaxLength(16);
            entity.Property(r => r.Augmentations).HasMaxLength(512);
            entity.Property(r => r.TreeHash).HasMaxLength(64);
            entity.Property(r => r.CostUsd).HasPrecision(12, 6);

            // Enums as text: a run's phase list is read by people during triage, and an integer
            // that shifts when an enum member is inserted is a migration hazard for no gain.
            entity.Property(r => r.Sensitivity).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.Condition).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.Priority).HasConversion<string>().HasMaxLength(16);

            entity.Property(r => r.PinnedModels)
                .HasColumnType("jsonb")
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonOptions),
                    json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new())
                .Metadata.SetValueComparer(DictionaryComparer);

            // The listing query: this user's runs, newest first.
            entity.HasIndex(r => new { r.OwnerSubject, r.CreatedAt });

            // Submission idempotency (plan §17): the same key from the same owner is the same run.
            entity.HasIndex(r => new { r.OwnerSubject, r.IdempotencyKey }).IsUnique();

            entity.HasMany(r => r.Phases).WithOne(p => p.Run!).HasForeignKey(p => p.RunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(r => r.Events).WithOne(e => e.Run!).HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunPhase>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.RunId).IsRequired().HasMaxLength(32);
            entity.Property(p => p.Phase).HasConversion<string>().HasMaxLength(32);
            entity.Property(p => p.State).HasConversion<string>().HasMaxLength(16);
            entity.Property(p => p.ArtifactKey).HasMaxLength(512);
            entity.Property(p => p.IdempotencyKey).HasMaxLength(256);
            entity.HasIndex(p => new { p.RunId, p.Phase }).IsUnique();
        });

        modelBuilder.Entity<RunEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RunId).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Phase).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.Message).HasMaxLength(1024);

            // WatchRun tails this by (run, id): the id is monotonic, so a resumed stream asks for
            // "everything after the last id I saw" and gets it in one index scan.
            entity.HasIndex(e => new { e.RunId, e.Id });
        });

        modelBuilder.Entity<ReviewTask>(entity =>
        {
            entity.HasKey(r => r.ReviewId);
            entity.Property(r => r.ReviewId).HasMaxLength(32);
            entity.Property(r => r.RunId).IsRequired().HasMaxLength(32);
            entity.Property(r => r.OwnerSubject).IsRequired().HasMaxLength(255);
            entity.Property(r => r.DocumentId).IsRequired().HasMaxLength(255);
            entity.Property(r => r.DocumentFamily).IsRequired().HasMaxLength(64);
            entity.Property(r => r.WorkflowRequestId).HasMaxLength(128);
            entity.Property(r => r.Status).IsRequired().HasMaxLength(16);
            entity.Property(r => r.Decision).HasConversion<string>().HasMaxLength(32);
            entity.Property(r => r.DecidedBy).HasMaxLength(255);
            entity.Property(r => r.Notes).HasMaxLength(4000);
            entity.Property(r => r.FindingsJson).HasColumnType("jsonb");
            entity.Property(r => r.EditsJson).HasColumnType("jsonb");
            entity.HasIndex(r => new { r.Status, r.CreatedAt });
            entity.HasIndex(r => r.RunId);
        });

        modelBuilder.Entity<ModelCall>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.RunId).IsRequired().HasMaxLength(32);
            entity.Property(c => c.Phase).HasConversion<string>().HasMaxLength(32);
            entity.Property(c => c.Role).HasConversion<string>().HasMaxLength(32);
            entity.Property(c => c.Sensitivity).HasConversion<string>().HasMaxLength(16);
            entity.Property(c => c.Unit).HasMaxLength(64);
            entity.Property(c => c.Provider).IsRequired().HasMaxLength(32);
            entity.Property(c => c.Model).IsRequired().HasMaxLength(128);
            entity.Property(c => c.LimitPool).IsRequired().HasMaxLength(64);
            entity.Property(c => c.PromptVersion).IsRequired().HasMaxLength(64);
            entity.Property(c => c.Outcome).IsRequired().HasMaxLength(32);
            entity.Property(c => c.CostUsd).HasPrecision(12, 6);
            entity.HasIndex(c => c.RunId);
            entity.HasIndex(c => new { c.Provider, c.At });
        });

        modelBuilder.Entity<Qualification>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.ModelKey).IsRequired().HasMaxLength(128);
            entity.Property(q => q.Role).HasConversion<string>().HasMaxLength(32);
            entity.Property(q => q.PromptVersion).IsRequired().HasMaxLength(64);
            entity.Property(q => q.MetricsJson).HasColumnType("jsonb");

            // The router's lookup key. Unique, because two answers to "is this model qualified for
            // this role at this prompt version?" would make routing depend on row order.
            entity.HasIndex(q => new { q.ModelKey, q.Role, q.PromptVersion }).IsUnique();
        });

        modelBuilder.Entity<FamilyInstinct>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Family).IsRequired().HasMaxLength(64);
            entity.Property(i => i.Pattern).IsRequired().HasMaxLength(512);
            entity.Property(i => i.Guidance).IsRequired().HasMaxLength(1024);
            entity.HasIndex(i => new { i.Family, i.Pattern }).IsUnique();
        });

        modelBuilder.Entity<RunResult>(entity =>
        {
            entity.HasKey(r => r.RunId);
            entity.Property(r => r.RunId).HasMaxLength(32);
            entity.Property(r => r.OwnerSubject).IsRequired().HasMaxLength(255);
            entity.Property(r => r.DocumentId).IsRequired().HasMaxLength(255);
            entity.Property(r => r.TreeHash).IsRequired().HasMaxLength(64);
            entity.Property(r => r.TreeJson).HasColumnType("jsonb");
            entity.Property(r => r.ParagraphsJson).HasColumnType("jsonb");
            entity.Property(r => r.AugmentationsJson).HasColumnType("jsonb");
            entity.HasIndex(r => new { r.OwnerSubject, r.DocumentId });
        });

        modelBuilder.Entity<Upload>(entity =>
        {
            entity.HasKey(u => u.UploadId);
            entity.Property(u => u.UploadId).HasMaxLength(64);
            entity.Property(u => u.OwnerSubject).IsRequired().HasMaxLength(255);
            entity.Property(u => u.FileName).IsRequired().HasMaxLength(512);
            entity.Property(u => u.MediaType).IsRequired().HasMaxLength(128);
            entity.Property(u => u.SourceExtension).IsRequired().HasMaxLength(16);
            entity.HasIndex(u => new { u.OwnerSubject, u.CreatedAt });
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, string>> DictionaryComparer =
        new(
            (left, right) => left != null && right != null && left.Count == right.Count && !left.Except(right).Any(),
            value => value.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
            value => new Dictionary<string, string>(value));
}
