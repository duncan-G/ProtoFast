using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class Character : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>A <see cref="CharacterKind"/> label from the default or the story's vocabulary.</summary>
    public required string Kind { get; set; }

    /// <summary>Avatar colour as an OKLCH hue, 0–359.</summary>
    public int Hue { get; set; }

    public string? Description { get; set; }

    public CharacterIdentity? Appearance { get; set; }

    public VoiceProfile? Voice { get; set; }

    public List<CharacterWardrobe> Wardrobes { get; set; } = [];

    /// <summary>The library's picture of it, if generated; stale once the sheet changed.</summary>
    public Guid? ImageAssetId { get; set; }

    public MediaAsset? Image { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
