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

    private const int UserIdLength = 255;

    private const int NameLength = 255;

    /// <summary>Enums are stored by name; the longest today is <c>Description</c>.</summary>
    private const int EnumLength = 32;

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentUpload> DocumentUploads => Set<DocumentUpload>();

    public DbSet<Story> Stories => Set<Story>();

    public DbSet<Act> Acts => Set<Act>();

    public DbSet<Scene> Scenes => Set<Scene>();

    public DbSet<SceneElement> SceneElements => Set<SceneElement>();

    public DbSet<SceneElementMention> SceneElementMentions => Set<SceneElementMention>();

    public DbSet<CastMember> CastMembers => Set<CastMember>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Prop> Props => Set<Prop>();

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

        ConfigureScreenplay(modelBuilder);
        ConfigureStoryLibrary(modelBuilder);
    }

    /// <summary>
    /// The screenplay tree: story → act → scene → element. Only the story carries a <c>UserId</c>;
    /// everything below is owned by it, and the shared query filter reaches the owner by following
    /// each row's required foreign key up the tree. Deleting a parent takes its subtree with it.
    /// </summary>
    private static void ConfigureScreenplay(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Story>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(s => s.Title).IsRequired().HasMaxLength(NameLength);
            entity.Property(s => s.SourceDocumentId).HasMaxLength(UploadIdLength);

            entity.HasOne<Document>()
                .WithMany()
                .HasForeignKey(s => s.SourceDocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            // The desk lists a user's stories most recently touched first.
            entity.HasIndex(s => new { s.UserId, s.DateLastModified });
            entity.HasIndex(s => s.SourceDocumentId);
        });

        modelBuilder.Entity<Act>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Title).HasMaxLength(NameLength);

            entity.HasOne(a => a.Story)
                .WithMany(s => s.Acts)
                .HasForeignKey(a => a.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(a => new { a.StoryId, a.Position });
            entity.ToTable(t => t.HasCheckConstraint("ck_acts_position_non_negative", "position >= 0"));
        });

        modelBuilder.Entity<Scene>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Title).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(s => s.Act)
                .WithMany(a => a.Scenes)
                .HasForeignKey(s => s.ActId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.ActId, s.Position });
            entity.ToTable(t => t.HasCheckConstraint("ck_scenes_position_non_negative", "position >= 0"));
        });

        modelBuilder.Entity<SceneElement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(e => e.TimeOfDay).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(e => e.TransitionKind).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(e => e.Parenthetical).HasMaxLength(NameLength);

            entity.HasOne(e => e.Scene)
                .WithMany(s => s.Elements)
                .HasForeignKey(e => e.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            // Library entries can be deleted out from under the flow; the editor shows the gap
            // ("MISSING LOCATION", "UNASSIGNED") and asks the writer to fill it.
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => e.LocationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Speaker)
                .WithMany()
                .HasForeignKey(e => e.SpeakerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => new { e.SceneId, e.Position });
            entity.HasIndex(e => e.LocationId);
            entity.HasIndex(e => e.SpeakerId);

            // Each kind owns its columns. Enum values are stored by name, so the literals here
            // are the SceneElementType / TimeOfDay / TransitionKind member names.
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_scene_elements_position_non_negative", "position >= 0");
                t.HasCheckConstraint(
                    "ck_scene_elements_heading_columns",
                    "(type = 'Heading') = (time_of_day IS NOT NULL) AND (location_id IS NULL OR type = 'Heading')");
                t.HasCheckConstraint(
                    "ck_scene_elements_transition_columns",
                    "(type = 'Transition') = (transition_kind IS NOT NULL)");
                t.HasCheckConstraint(
                    "ck_scene_elements_dialogue_columns",
                    "type = 'Dialogue' OR (speaker_id IS NULL AND parenthetical IS NULL)");
                t.HasCheckConstraint(
                    "ck_scene_elements_text_columns",
                    "(type IN ('Heading', 'Transition')) = (text IS NULL)");
            });
        });

        modelBuilder.Entity<SceneElementMention>(entity =>
        {
            entity.HasKey(m => m.Id);

            entity.HasOne(m => m.SceneElement)
                .WithMany(e => e.Mentions)
                .HasForeignKey(m => m.SceneElementId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a library entry drops its mentions; the "@Name" stays in the prose as
            // plain text, which is how the editor renders a name it can no longer resolve.
            entity.HasOne(m => m.CastMember)
                .WithMany()
                .HasForeignKey(m => m.CastMemberId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.Prop)
                .WithMany()
                .HasForeignKey(m => m.PropId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(m => m.SceneElementId);
            entity.HasIndex(m => m.CastMemberId);
            entity.HasIndex(m => m.PropId);

            entity.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_scene_element_mentions_one_target",
                    "(cast_member_id IS NULL) <> (prop_id IS NULL)");
                t.HasCheckConstraint(
                    "ck_scene_element_mentions_span",
                    "\"offset\" >= 0 AND length >= 2");
            });
        });
    }

    /// <summary>
    /// The story library: the cast, locations and props every scene of a story shares. Names are
    /// unique per story because <c>@Name</c> references resolve by them. The index compares
    /// exactly; the queries that check for a clash before an insert compare case-insensitively,
    /// as the editor does.
    /// </summary>
    private static void ConfigureStoryLibrary(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CastMember>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(NameLength);
            entity.Property(c => c.Kind).HasConversion<string>().HasMaxLength(EnumLength);

            entity.HasOne(c => c.Story)
                .WithMany(s => s.Cast)
                .HasForeignKey(c => c.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => new { c.StoryId, c.Name }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint("ck_cast_members_hue", "hue BETWEEN 0 AND 359"));
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Name).IsRequired().HasMaxLength(NameLength);
            entity.Property(l => l.Setting).HasConversion<string>().HasMaxLength(EnumLength);

            entity.HasOne(l => l.Story)
                .WithMany(s => s.Locations)
                .HasForeignKey(l => l.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => new { l.StoryId, l.Name }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint("ck_locations_hue", "hue BETWEEN 0 AND 359"));
        });

        modelBuilder.Entity<Prop>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(p => p.Story)
                .WithMany(s => s.Props)
                .HasForeignKey(p => p.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(p => new { p.StoryId, p.Name }).IsUnique();
        });
    }
}
