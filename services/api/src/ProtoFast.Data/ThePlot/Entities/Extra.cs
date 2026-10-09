using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// Someone uncast who appears in pictures: one person ("the barman") or a group ("commuters").
/// Described rather than identified, with no voice; the same entry keeps them alike across scenes.
/// </summary>
public sealed class Extra : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>Who they are and how many: "three teenage boys on bikes".</summary>
    public string? Description { get; set; }

    public string? Wardrobe { get; set; }

    /// <summary>The library's picture of them, if generated; stale once the sheet changed.</summary>
    public Guid? ImageAssetId { get; set; }

    public MediaAsset? Image { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
