namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// An <c>@Name</c> in a beat's text, pointing at a character, prop or location. Owned by its
/// <see cref="SceneElement"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class SceneElementMention
{
    public Guid Id { get; set; }

    public Guid SceneElementId { get; set; }

    public SceneElement SceneElement { get; set; } = null!;

    public Guid? CharacterId { get; set; }

    public Character? Character { get; set; }

    public Guid? PropId { get; set; }

    public Prop? Prop { get; set; }

    public Guid? LocationId { get; set; }

    public Location? Location { get; set; }

    /// <summary>Index of the <c>@</c> in the beat's text.</summary>
    public int Offset { get; set; }

    /// <summary>Length of the reference, <c>@</c> included.</summary>
    public int Length { get; set; }
}
