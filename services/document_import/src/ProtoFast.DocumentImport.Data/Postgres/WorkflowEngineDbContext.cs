using Microsoft.EntityFrameworkCore;
using ProtoFast.DocumentImport.Data.Postgres.Entities;

namespace ProtoFast.DocumentImport.Data.Postgres;

/// <summary>
/// The engine's slice of the <c>protofast</c> database. Registry content and artifacts are frozen
/// in the object store; this holds what changes or is queried.
/// </summary>
public sealed class WorkflowEngineDbContext(DbContextOptions<WorkflowEngineDbContext> options) : DbContext(options)
{
    public const string Schema = "engine";

    private const int IdLength = 200;
    private const int HashLength = 64;
    private const int EnumLength = 16;

    public DbSet<RegistryEntry> RegistryEntries => Set<RegistryEntry>();

    public DbSet<DocumentFamilyExecutor> DocumentFamilyExecutors => Set<DocumentFamilyExecutor>();

    public DbSet<DocumentFamilyVerifier> DocumentFamilyVerifiers => Set<DocumentFamilyVerifier>();

    public DbSet<DocumentFamilySkill> DocumentFamilySkills => Set<DocumentFamilySkill>();

    public DbSet<RunEntry> Runs => Set<RunEntry>();

    public DbSet<StageRecordEntry> StageRecords => Set<StageRecordEntry>();

    public DbSet<RunDecisionEntry> RunDecisions => Set<RunDecisionEntry>();

    public DbSet<RunMessageEntry> RunMessages => Set<RunMessageEntry>();

    public DbSet<RunSystemPromptEntry> RunSystemPrompts => Set<RunSystemPromptEntry>();

    public DbSet<RunProgressEntry> RunProgress => Set<RunProgressEntry>();

    public DbSet<StagePolicyEntry> StagePolicies => Set<StagePolicyEntry>();

    public DbSet<DocumentFamilyPolicyEntry> DocumentFamilyPolicies => Set<DocumentFamilyPolicyEntry>();

    public DbSet<DocumentFamilyGenerationEntry> DocumentFamilyGenerations => Set<DocumentFamilyGenerationEntry>();

    public DbSet<MinedWorkflowDraftEntry> MinedWorkflowDrafts => Set<MinedWorkflowDraftEntry>();

    public DbSet<DocumentFamilyEntry> DocumentFamilies => Set<DocumentFamilyEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Keyed by the bare family name: what an operator wrote about it outlives every generation.
        modelBuilder.Entity<DocumentFamilyEntry>(entity =>
        {
            entity.HasKey(e => e.Family);
            entity.Property(e => e.Family).HasMaxLength(IdLength);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(IdLength);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(IdLength);
        });

        modelBuilder.Entity<RegistryEntry>(entity =>
        {
            // The key is what makes version assignment safe: two publishers that pick the same
            // next version cannot both insert it.
            entity.HasKey(e => new { e.Kind, e.Id, e.Version });
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(EnumLength);
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

        modelBuilder.Entity<DocumentFamilySkill>(entity =>
        {
            entity.HasKey(e => new { e.Family, e.SkillId, e.SkillVersion });
            entity.Property(e => e.Family).HasMaxLength(IdLength);
            entity.Property(e => e.SkillId).HasMaxLength(IdLength);
        });

        modelBuilder.Entity<RunEntry>(entity =>
        {
            entity.HasKey(e => e.RunId);
            entity.Property(e => e.RunId).HasMaxLength(IdLength);
            entity.Property(e => e.Family).IsRequired().HasMaxLength(IdLength);
            entity.Property(e => e.Facets).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.Mode).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(e => e.TraceId).HasMaxLength(IdLength);

            // The miner and Context() read a family's most recently closed runs.
            entity.HasIndex(e => new { e.Family, e.Mode, e.ClosedAt });
            // The console lists runs newest first.
            entity.HasIndex(e => e.OpenedAt).IsDescending();
        });

        modelBuilder.Entity<StageRecordEntry>(entity =>
        {
            entity.HasKey(e => e.Sequence);
            entity.Property(e => e.RunId).IsRequired().HasMaxLength(IdLength);
            entity.Property(e => e.StageId).IsRequired().HasMaxLength(IdLength);
            entity.Property(e => e.Record).IsRequired().HasColumnType("jsonb");
            entity.HasOne<RunEntry>().WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.RunId, e.Sequence });
        });

        modelBuilder.Entity<RunDecisionEntry>(entity =>
        {
            entity.HasKey(e => e.Sequence);
            entity.Property(e => e.RunId).IsRequired().HasMaxLength(IdLength);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(IdLength);
            entity.Property(e => e.Choice).IsRequired();
            entity.Property(e => e.Rationale).IsRequired();
            entity.HasOne<RunEntry>().WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.RunId, e.Sequence });
        });

        // The key is what makes a resumed run's re-append of the same message a no-op.
        modelBuilder.Entity<RunMessageEntry>(entity =>
        {
            entity.HasKey(e => new { e.RunId, e.Sequence });
            entity.Property(e => e.RunId).HasMaxLength(IdLength);
            entity.Property(e => e.Message).IsRequired().HasColumnType("jsonb");
            entity.HasOne<RunEntry>().WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunSystemPromptEntry>(entity =>
        {
            entity.HasKey(e => new { e.RunId, e.FromSequence });
            entity.Property(e => e.RunId).HasMaxLength(IdLength);
            entity.Property(e => e.Prompt).IsRequired();
            entity.HasOne<RunEntry>().WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
        });

        // Not keyed to a run: a source outlives the runs that fail on it.
        modelBuilder.Entity<RunProgressEntry>(entity =>
        {
            entity.HasKey(e => e.SourceId);
            entity.Property(e => e.SourceId).HasMaxLength(IdLength);
            entity.Property(e => e.Phase).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(e => e.RunId).HasMaxLength(IdLength);
            entity.Property(e => e.StageId).HasMaxLength(IdLength);
            entity.Property(e => e.ResultId).HasMaxLength(IdLength);
            entity.Property(e => e.Cost).HasPrecision(18, 6);
            entity.HasIndex(e => e.RunId);
        });

        modelBuilder.Entity<StagePolicyEntry>(entity =>
        {
            entity.HasKey(e => new { e.Family, e.StageId });
            entity.Property(e => e.Family).HasMaxLength(IdLength);
            entity.Property(e => e.StageId).HasMaxLength(IdLength);
            entity.Property(e => e.Ladder).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.Primary).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(e => e.Shadow).HasConversion<string>().HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<DocumentFamilyPolicyEntry>(entity =>
        {
            entity.HasKey(e => e.Family);
            entity.Property(e => e.Family).HasMaxLength(IdLength);
            entity.Property(e => e.Mode).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(e => e.WorkflowId).HasMaxLength(IdLength);
        });

        modelBuilder.Entity<DocumentFamilyGenerationEntry>(entity =>
        {
            entity.HasKey(e => e.Family);
            entity.Property(e => e.Family).HasMaxLength(IdLength);
        });

        modelBuilder.Entity<MinedWorkflowDraftEntry>(entity =>
        {
            entity.HasKey(e => new { e.WorkflowId, e.WorkflowVersion });
            entity.Property(e => e.WorkflowId).HasMaxLength(IdLength);
            entity.Property(e => e.Family).IsRequired().HasMaxLength(IdLength);
            entity.Property(e => e.Mined).IsRequired().HasColumnType("jsonb");
        });
    }
}
