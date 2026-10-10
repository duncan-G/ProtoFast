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

    public DbSet<Extra> Extras => Set<Extra>();

    public DbSet<CharacterWardrobe> CharacterWardrobes => Set<CharacterWardrobe>();

    public DbSet<SceneAppearance> SceneAppearances => Set<SceneAppearance>();

    public DbSet<SceneLookChange> SceneLookChanges => Set<SceneLookChange>();

    public DbSet<ScenePropState> ScenePropStates => Set<ScenePropState>();

    public DbSet<SceneExtra> SceneExtras => Set<SceneExtra>();

    public DbSet<Frame> Frames => Set<Frame>();

    public DbSet<Cue> Cues => Set<Cue>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    public DbSet<Narrator> Narrators => Set<Narrator>();

    public DbSet<SceneLocationState> SceneLocationStates => Set<SceneLocationState>();

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
        ConfigureDirection(modelBuilder);
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

        modelBuilder.Entity<Narrator>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Name).IsRequired().HasMaxLength(NameLength);
            entity.OwnsOne(n => n.Voice, voice => voice.ToJson());
            entity.Navigation(n => n.Voice).IsRequired();

            entity.HasOne(n => n.Story)
                .WithMany(s => s.Narrators)
                .HasForeignKey(n => n.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(n => new { n.StoryId, n.Position });
            entity.ToTable(t => t.HasCheckConstraint("ck_narrators_position_non_negative", "position >= 0"));
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
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_scenes_position_non_negative", "position >= 0");
                t.HasCheckConstraint("ck_scenes_story_day", "story_day IS NULL OR story_day >= 1");
            });
            entity.Property(s => s.Shot).HasMaxLength(LabelLength);
            entity.Property(s => s.Angle).HasMaxLength(LabelLength);
            entity.Property(s => s.Movement).HasMaxLength(LabelLength);
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

            entity.OwnsOne(c => c.Appearance, appearance => appearance.ToJson());
            entity.OwnsOne(c => c.Voice, voice => voice.ToJson());

            // Deleting an asset leaves it unpictured, to be generated again.
            entity.HasOne(c => c.Image)
                .WithMany()
                .HasForeignKey(c => c.ImageAssetId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(c => c.ImageAssetId);

            entity.HasIndex(c => new { c.StoryId, c.Name }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint("ck_characters_hue", "hue BETWEEN 0 AND 359"));
        });

        modelBuilder.Entity<CharacterWardrobe>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Label).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(w => w.Character)
                .WithMany(c => c.Wardrobes)
                .HasForeignKey(w => w.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(w => new { w.CharacterId, w.Label }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint("ck_character_wardrobes_position_non_negative", "position >= 0"));
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

            entity.OwnsOne(l => l.Appearance, appearance =>
            {
                appearance.ToJson();
                appearance.OwnsMany(a => a.Lighting);
            });
            entity.OwnsOne(l => l.Acoustics, acoustics => acoustics.ToJson());

            // Deleting an asset leaves it unpictured, to be generated again.
            entity.HasOne(l => l.Image)
                .WithMany()
                .HasForeignKey(l => l.ImageAssetId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(l => l.ImageAssetId);

            entity.HasIndex(l => new { l.StoryId, l.Name }).IsUnique();
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

            // Deleting an asset leaves it unpictured, to be generated again.
            entity.HasOne(p => p.Image)
                .WithMany()
                .HasForeignKey(p => p.ImageAssetId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(p => p.ImageAssetId);

            entity.HasIndex(p => new { p.StoryId, p.Name }).IsUnique();
        });

        modelBuilder.Entity<Extra>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(e => e.Story)
                .WithMany(s => s.Extras)
                .HasForeignKey(e => e.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Image)
                .WithMany()
                .HasForeignKey(e => e.ImageAssetId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.ImageAssetId);

            entity.HasIndex(e => new { e.StoryId, e.Name }).IsUnique();
        });
    }

    /// <summary>
    /// The director layer over a scene. Every row hangs off the scene or a frame by a required
    /// foreign key, so the scene's owner scopes it; deleting an element takes its cue and the
    /// frames it bounds with it.
    /// </summary>
    private static void ConfigureDirection(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SceneAppearance>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Travel).IsRequired().HasMaxLength(LabelLength);

            entity.HasOne(a => a.Scene)
                .WithMany(s => s.Appearances)
                .HasForeignKey(a => a.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Character)
                .WithMany()
                .HasForeignKey(a => a.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            // A wardrobe the sheet drops leaves the look without one, to be chosen again.
            entity.HasOne(a => a.Wardrobe)
                .WithMany()
                .HasForeignKey(a => a.WardrobeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.ChangeElement)
                .WithMany()
                .HasForeignKey(a => a.ChangeElementId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.Image)
                .WithMany()
                .HasForeignKey(a => a.ImageAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(a => new { a.SceneId, a.CharacterId }).IsUnique();
            entity.HasIndex(a => a.WardrobeId);
            entity.HasIndex(a => a.ChangeElementId);
            entity.HasIndex(a => a.ImageAssetId);
        });

        modelBuilder.Entity<SceneLookChange>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.WardrobeChange).IsRequired();
            entity.Property(c => c.AppearanceChange).IsRequired();

            entity.HasOne(c => c.Scene)
                .WithMany(s => s.LookChanges)
                .HasForeignKey(c => c.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Character)
                .WithMany()
                .HasForeignKey(c => c.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting the line takes the change with it.
            entity.HasOne(c => c.Element)
                .WithMany()
                .HasForeignKey(c => c.ElementId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Wardrobe)
                .WithMany()
                .HasForeignKey(c => c.WardrobeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(c => c.Image)
                .WithMany()
                .HasForeignKey(c => c.ImageAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(c => new { c.SceneId, c.CharacterId, c.ElementId }).IsUnique();
            entity.HasIndex(c => c.CharacterId);
            entity.HasIndex(c => c.ElementId);
            entity.HasIndex(c => c.WardrobeId);
            entity.HasIndex(c => c.ImageAssetId);
        });

        modelBuilder.Entity<ScenePropState>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.HasOne(p => p.Scene)
                .WithMany(s => s.PropStates)
                .HasForeignKey(p => p.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Prop)
                .WithMany()
                .HasForeignKey(p => p.PropId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.ChangeElement)
                .WithMany()
                .HasForeignKey(p => p.ChangeElementId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(p => new { p.SceneId, p.PropId }).IsUnique();
            entity.HasIndex(p => p.ChangeElementId);
        });

        modelBuilder.Entity<SceneExtra>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Scene)
                .WithMany(s => s.Extras)
                .HasForeignKey(e => e.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Extra)
                .WithMany()
                .HasForeignKey(e => e.ExtraId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.SceneId, e.ExtraId }).IsUnique();
            entity.HasIndex(e => e.ExtraId);
        });

        modelBuilder.Entity<SceneLocationState>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Change).IsRequired();

            entity.HasOne(l => l.Scene)
                .WithMany(s => s.LocationStates)
                .HasForeignKey(l => l.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(l => l.Location)
                .WithMany()
                .HasForeignKey(l => l.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => new { l.SceneId, l.LocationId }).IsUnique();
            entity.HasIndex(l => l.LocationId);
        });

        modelBuilder.Entity<Frame>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.TimeOfDay).HasMaxLength(LabelLength);
            entity.Property(p => p.Shot).HasMaxLength(LabelLength);
            entity.Property(p => p.Angle).HasMaxLength(LabelLength);
            entity.Property(p => p.Movement).HasMaxLength(LabelLength);
            entity.Property(p => p.LocationChange).IsRequired();
            entity.Property(p => p.Camera).IsRequired();

            entity.HasOne(p => p.Scene)
                .WithMany(s => s.Frames)
                .HasForeignKey(p => p.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.FirstElement)
                .WithMany()
                .HasForeignKey(p => p.FirstElementId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.LastElement)
                .WithMany()
                .HasForeignKey(p => p.LastElementId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Location)
                .WithMany()
                .HasForeignKey(p => p.LocationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Deleting an asset leaves it unpictured, to be generated again.
            entity.HasOne(p => p.Image)
                .WithMany()
                .HasForeignKey(p => p.ImageAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(p => new { p.SceneId, p.Position });
            entity.HasIndex(p => p.ImageAssetId);
            entity.HasIndex(p => p.FirstElementId);
            entity.HasIndex(p => p.LastElementId);
            entity.HasIndex(p => p.LocationId);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_frames_position_non_negative", "position >= 0");
                t.HasCheckConstraint("ck_frames_hold_ms", "hold_ms IS NULL OR hold_ms > 0");
            });
        });

        modelBuilder.Entity<FramePlacement>(entity =>
        {
            entity.ToTable("frame_placements");
            entity.HasKey(p => new { p.FrameId, p.CharacterId });
            entity.Property(p => p.Screen).IsRequired().HasMaxLength(LabelLength);
            entity.Property(p => p.Facing).IsRequired().HasMaxLength(LabelLength);
            entity.Property(p => p.Moving).IsRequired().HasMaxLength(LabelLength);
            entity.Property(p => p.Looking).IsRequired().HasMaxLength(NameLength);
            entity.Property(p => p.Pose).IsRequired().HasMaxLength(NameLength);
            entity.Property(p => p.Expression).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(p => p.Frame)
                .WithMany(frame => frame.Placements)
                .HasForeignKey(p => p.FrameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Character)
                .WithMany()
                .HasForeignKey(p => p.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            // A wardrobe the sheet drops sends the frame back to the scene's.
            entity.HasOne(p => p.Wardrobe)
                .WithMany()
                .HasForeignKey(p => p.WardrobeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(p => p.CharacterId);
            entity.HasIndex(p => p.WardrobeId);
        });

        modelBuilder.Entity<FrameProp>(entity =>
        {
            entity.ToTable("frame_props");
            entity.HasKey(p => new { p.FrameId, p.PropId });

            entity.HasOne(p => p.Frame)
                .WithMany(frame => frame.Props)
                .HasForeignKey(p => p.FrameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Prop)
                .WithMany()
                .HasForeignKey(p => p.PropId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(p => p.PropId);
        });

        modelBuilder.Entity<FrameExtra>(entity =>
        {
            entity.ToTable("frame_extras");
            entity.HasKey(e => new { e.FrameId, e.ExtraId });
            entity.Property(e => e.Screen).IsRequired().HasMaxLength(LabelLength);
            entity.Property(e => e.Facing).IsRequired().HasMaxLength(LabelLength);
            entity.Property(e => e.Moving).IsRequired().HasMaxLength(LabelLength);
            entity.Property(e => e.Looking).IsRequired().HasMaxLength(NameLength);
            entity.Property(e => e.Pose).IsRequired().HasMaxLength(NameLength);

            entity.HasOne(e => e.Frame)
                .WithMany(frame => frame.Extras)
                .HasForeignKey(e => e.FrameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Extra)
                .WithMany()
                .HasForeignKey(e => e.ExtraId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ExtraId);
        });

        modelBuilder.Entity<Cue>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Performance).IsRequired();

            entity.HasOne(c => c.Scene)
                .WithMany(s => s.Cues)
                .HasForeignKey(c => c.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Element)
                .WithMany()
                .HasForeignKey(c => c.ElementId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting an asset leaves the cue unvoiced, to be generated again.
            entity.HasOne(c => c.Audio)
                .WithMany()
                .HasForeignKey(c => c.AudioAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            // Deleting a narrator hands their lines back to the story's first.
            entity.HasOne(c => c.Narrator)
                .WithMany()
                .HasForeignKey(c => c.NarratorId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(c => c.ElementId).IsUnique();
            entity.HasIndex(c => c.SceneId);
            entity.HasIndex(c => c.AudioAssetId);
            entity.HasIndex(c => c.NarratorId);
        });

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(a => a.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(a => a.StorageKey).IsRequired().HasMaxLength(512);
            entity.Property(a => a.ContentType).IsRequired().HasMaxLength(NameLength);
            entity.Property(a => a.ContentHash).IsRequired().HasMaxLength(LabelLength);
            entity.Property(a => a.RequestHash).IsRequired().HasMaxLength(LabelLength);
            entity.Property(a => a.Provider).IsRequired().HasMaxLength(LabelLength);
            entity.Property(a => a.Model).IsRequired().HasMaxLength(NameLength);
            entity.Property(a => a.Voice).HasMaxLength(NameLength);
            entity.OwnsMany(a => a.References, references => references.ToJson());

            entity.HasOne(a => a.Story)
                .WithMany()
                .HasForeignKey(a => a.StoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // The media cache: the same request in the same story is rendered once.
            entity.HasIndex(a => new { a.StoryId, a.Kind, a.RequestHash }).IsUnique();
        });
    }
}
