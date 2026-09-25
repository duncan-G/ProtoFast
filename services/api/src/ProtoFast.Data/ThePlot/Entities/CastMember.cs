using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// Anyone or anything with a voice in a story — people, robots, animals. Part of the story
/// library: a cast member can speak dialogue beats and be <c>@</c>-mentioned in any scene.
/// </summary>
public sealed class CastMember : IDateStamped
{
    public Guid Id { get; set; }

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    /// <summary>Unique within the story, since <c>@Name</c> references resolve by it.</summary>
    public required string Name { get; set; }

    /// <summary>Sets the avatar's shape in the editor.</summary>
    public CastMemberKind Kind { get; set; }

    /// <summary>The avatar's colour, as an OKLCH hue in degrees (0–359).</summary>
    public int Hue { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
