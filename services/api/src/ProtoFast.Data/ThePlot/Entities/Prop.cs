using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class Prop : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>The library's picture of it, if generated; stale once the sheet changed.</summary>
    public Guid? ImageAssetId { get; set; }

    public MediaAsset? Image { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
