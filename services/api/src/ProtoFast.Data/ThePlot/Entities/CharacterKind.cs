using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>What a character is ("Human", "Robot"). Each story keeps its own list.</summary>
public sealed class CharacterKind : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public required string Label { get; set; }

    /// <summary>The avatar shape of characters of this kind.</summary>
    public AvatarShape AvatarShape { get; set; }

    public int Position { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
