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

    private const int LabelLength = 64;

    /// <summary>Enums are stored by name, so renaming a member needs a data migration.</summary>
    private const int EnumLength = 32;

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentUpload> DocumentUploads => Set<DocumentUpload>();

    public DbSet<Story> Stories => Set<Story>();

    public DbSet<Container> Containers => Set<Container>();

    public DbSet<Scene> Scenes => Set<Scene>();

    public DbSet<SceneElement> SceneElements => Set<SceneElement>();

    public DbSet<SceneElementMention> SceneElementMentions => Set<SceneElementMention>();

    public DbSet<Character> Characters => Set<Character>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Prop> Props => Set<Prop>();

    public DbSet<LocationPart> LocationParts => Set<LocationPart>();

    public DbSet<LocationObject> LocationObjects => Set<LocationObject>();

    public DbSet<LocationObservation> LocationObservations => Set<LocationObservation>();

    public DbSet<WorldModelBuild> WorldModelBuilds => Set<WorldModelBuild>();

    public DbSet<WorldModelBuildStep> WorldModelBuildSteps => Set<WorldModelBuildStep>();

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
        ConfigureWorldModel(modelBuilder);
    }

    private static void ConfigureScreenplay(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Story>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(s => s.Title).IsRequired().HasMaxLength(NameLength);

            entity.OwnsOne(s => s.Vocabulary, vocabulary =>
            {
                vocabulary.ToJson();
                vocabulary.OwnsMany(v => v.CharacterKinds,
                    kind => kind.Property(k => k.AvatarShape).HasConversion<string>());
            });
            entity.Navigation(s => s.Vocabulary).IsRequired();

            entity.HasIndex(s => new { s.UserId, s.DateLastModified });
        });

        modelBuilder.Entity<Container>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(c => c.Label).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(c => c.Story)
                .WithMany(s => s.Containers)
                .HasForeignKey(c => c.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Positions are not unique, so a reorder can rewrite them in any order in one save.
            entity.HasIndex(c => new { c.StoryId, c.Position });
            entity.ToTable(t => t.HasCheckConstraint("ck_containers_position_non_negative", "position >= 0"));
        });

        modelBuilder.Entity<Scene>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(s => s.Title).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(s => s.Container)
                .WithMany(c => c.Scenes)
                .HasForeignKey(s => s.ContainerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.ContainerId, s.Position });
            entity.ToTable(t => t.HasCheckConstraint("ck_scenes_position_non_negative", "position >= 0"));
        });

        modelBuilder.Entity<SceneElement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(e => e.Extension).HasMaxLength(LabelLength);
            entity.Property(e => e.Parenthetical).HasMaxLength(NameLength);
            entity.Property(e => e.TimeOfDay).HasMaxLength(LabelLength);
            entity.Property(e => e.Transition).HasMaxLength(LabelLength);

            entity.HasOne(e => e.Scene)
                .WithMany(s => s.Elements)
                .HasForeignKey(e => e.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a location or character leaves the heading or line unassigned.
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

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_scene_elements_position_non_negative", "position >= 0");
                t.HasCheckConstraint(
                    "ck_scene_elements_heading_columns",
                    "type = 'Heading' OR (location_id IS NULL AND time_of_day IS NULL)");
                t.HasCheckConstraint(
                    "ck_scene_elements_transition_columns",
                    "type = 'Transition' OR transition IS NULL");
                t.HasCheckConstraint(
                    "ck_scene_elements_dialogue_columns",
                    "type = 'Dialogue' OR (speaker_id IS NULL AND extension IS NULL AND parenthetical IS NULL)");
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

            // Deleting a character, prop or location drops its mentions; the "@Name" stays in the text.
            entity.HasOne(m => m.Character)
                .WithMany()
                .HasForeignKey(m => m.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.Prop)
                .WithMany()
                .HasForeignKey(m => m.PropId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.Location)
                .WithMany()
                .HasForeignKey(m => m.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(m => m.SceneElementId);
            entity.HasIndex(m => m.CharacterId);
            entity.HasIndex(m => m.PropId);
            entity.HasIndex(m => m.LocationId);

            entity.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_scene_element_mentions_one_target",
                    "num_nonnulls(character_id, prop_id, location_id) = 1");
                t.HasCheckConstraint(
                    "ck_scene_element_mentions_span",
                    "\"offset\" >= 0 AND length >= CASE WHEN is_tag THEN 1 ELSE 2 END");
            });
        });
    }

    /// <summary>Names are unique per story because <c>@Name</c> references resolve by them.</summary>
    private static void ConfigureStoryLibrary(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Character>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(NameLength);
            entity.Property(c => c.Kind).IsRequired().HasMaxLength(LabelLength);

            entity.HasOne(c => c.Story)
                .WithMany(s => s.Characters)
                .HasForeignKey(c => c.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => new { c.StoryId, c.Name }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint("ck_characters_hue", "hue BETWEEN 0 AND 359"));
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(l => l.Name).IsRequired().HasMaxLength(NameLength);
            entity.Property(l => l.Setting).HasConversion<string>().HasMaxLength(EnumLength);

            entity.HasOne(l => l.Story)
                .WithMany(s => s.Locations)
                .HasForeignKey(l => l.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(l => l.InsideLocation)
                .WithMany()
                .HasForeignKey(l => l.InsideLocationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(l => new { l.StoryId, l.Name }).IsUnique();
            entity.HasIndex(l => l.InsideLocationId);
            entity.ToTable(t => t.HasCheckConstraint("ck_locations_hue", "hue BETWEEN 0 AND 359"));
        });

        modelBuilder.Entity<Prop>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(p => p.Story)
                .WithMany(s => s.Props)
                .HasForeignKey(p => p.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(p => new { p.StoryId, p.Name }).IsUnique();
        });
    }

    /// <summary>
    /// The location world model (docs/design/location-world-model.md): parts and objects are
    /// nodes, the timeless edges are columns, and every edge that changes over story time is an
    /// observation row tied to its beat. Facts are never stored; code folds them from the rows.
    /// </summary>
    private static void ConfigureWorldModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocationPart>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(NameLength);
            entity.Property(p => p.Origin).HasConversion<string>().HasMaxLength(EnumLength);

            entity.HasOne(p => p.Location)
                .WithMany(l => l.Parts)
                .HasForeignKey(p => p.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.ParentPart)
                .WithMany()
                .HasForeignKey(p => p.ParentPartId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(p => p.LocationId);
            entity.HasIndex(p => p.ParentPartId);
            entity.ToTable(t => t.HasCheckConstraint("ck_location_parts_position_non_negative", "position IS NULL OR position >= 0"));
        });

        modelBuilder.Entity<LocationObject>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(o => o.Name).IsRequired().HasMaxLength(NameLength);
            entity.Property(o => o.Kind).HasConversion<string>().HasMaxLength(EnumLength);

            entity.HasOne(o => o.Story)
                .WithMany()
                .HasForeignKey(o => o.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.Prop)
                .WithMany()
                .HasForeignKey(o => o.PropId)
                .OnDelete(DeleteBehavior.SetNull);

            // Not unique: names are matched in code, aliases included.
            entity.HasIndex(o => new { o.StoryId, o.Name });
            entity.HasIndex(o => o.PropId);
        });

        modelBuilder.Entity<LocationObservation>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Edge).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(o => o.Basis).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(o => o.Origin).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(o => o.Property).HasMaxLength(NameLength);
            entity.Property(o => o.Value).HasMaxLength(NameLength);

            entity.HasOne(o => o.SceneElement)
                .WithMany(e => e.Observations)
                .HasForeignKey(o => o.SceneElementId)
                .OnDelete(DeleteBehavior.Cascade);

            // An observation is about its nodes, so it goes with any of them; only the way through survives.
            entity.HasOne(o => o.Object)
                .WithMany()
                .HasForeignKey(o => o.ObjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.Character)
                .WithMany()
                .HasForeignKey(o => o.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.PlaceLocation)
                .WithMany()
                .HasForeignKey(o => o.PlaceLocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.PlacePart)
                .WithMany()
                .HasForeignKey(o => o.PlacePartId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.OtherLocation)
                .WithMany()
                .HasForeignKey(o => o.OtherLocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.OtherPart)
                .WithMany()
                .HasForeignKey(o => o.OtherPartId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.ViaObject)
                .WithMany()
                .HasForeignKey(o => o.ViaObjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(o => o.SceneElementId);
            entity.HasIndex(o => o.ObjectId);
            entity.HasIndex(o => o.CharacterId);
            entity.HasIndex(o => o.PlaceLocationId);

            entity.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_location_observations_at_columns",
                    "edge <> 'At' OR (object_id IS NOT NULL AND character_id IS NULL AND other_location_id IS NULL "
                    + "AND other_part_id IS NULL AND via_object_id IS NULL AND property IS NULL AND value IS NULL)");
                t.HasCheckConstraint(
                    "ck_location_observations_state_columns",
                    "edge <> 'State' OR (object_id IS NOT NULL AND property IS NOT NULL AND value IS NOT NULL "
                    + "AND place_location_id IS NULL AND place_part_id IS NULL AND character_id IS NULL "
                    + "AND other_location_id IS NULL AND other_part_id IS NULL AND via_object_id IS NULL)");
                t.HasCheckConstraint(
                    "ck_location_observations_connects_columns",
                    "edge <> 'Connects' OR (place_location_id IS NOT NULL AND other_location_id IS NOT NULL "
                    + "AND object_id IS NULL AND character_id IS NULL AND property IS NULL)");
                t.HasCheckConstraint(
                    "ck_location_observations_character_at_columns",
                    "edge <> 'CharacterAt' OR (character_id IS NOT NULL AND place_location_id IS NOT NULL "
                    + "AND object_id IS NULL AND other_location_id IS NULL AND other_part_id IS NULL "
                    + "AND via_object_id IS NULL AND property IS NULL AND value IS NULL)");
                t.HasCheckConstraint(
                    "ck_location_observations_beat_at_columns",
                    "edge <> 'BeatAt' OR (place_location_id IS NOT NULL AND object_id IS NULL AND character_id IS NULL "
                    + "AND other_location_id IS NULL AND other_part_id IS NULL AND via_object_id IS NULL "
                    + "AND property IS NULL AND value IS NULL)");
                t.HasCheckConstraint(
                    "ck_location_observations_parts_have_locations",
                    "(place_part_id IS NULL OR place_location_id IS NOT NULL) AND (other_part_id IS NULL OR other_location_id IS NOT NULL)");
            });
        });

        modelBuilder.Entity<WorldModelBuild>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(b => b.Status).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(b => b.Cost).HasPrecision(18, 6);
            entity.Property(b => b.TraceId).HasMaxLength(LabelLength);

            entity.HasOne(b => b.Story)
                .WithMany()
                .HasForeignKey(b => b.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(b => new { b.StoryId, b.DateCreated });
            entity.HasIndex(b => b.DateCreated);
            entity.ToTable(t => t.HasCheckConstraint(
                "ck_world_model_builds_counts_non_negative",
                "scenes_total >= 0 AND scenes_done >= 0 AND deliveries >= 0 AND part_count >= 0 AND object_count >= 0 AND observation_count >= 0"));
        });

        modelBuilder.Entity<WorldModelBuildStep>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(s => s.Title).IsRequired().HasMaxLength(NameLength);
            entity.Property(s => s.ModelId).HasMaxLength(NameLength);
            entity.Property(s => s.Cost).HasPrecision(18, 6);

            entity.HasOne(s => s.Build)
                .WithMany(b => b.Steps)
                .HasForeignKey(s => s.BuildId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.Scene)
                .WithMany()
                .HasForeignKey(s => s.SceneId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(s => new { s.BuildId, s.Sequence });
            entity.HasIndex(s => s.SceneId);
        });
    }
}
