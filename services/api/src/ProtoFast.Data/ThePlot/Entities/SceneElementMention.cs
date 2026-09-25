namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// An <c>@Name</c> in a beat's text, pointing at a cast member or a prop. Owned by its
/// <see cref="SceneElement"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class SceneElementMention
{
    public Guid Id { get; set; }

    public Guid SceneElementId { get; set; }

    public SceneElement SceneElement { get; set; } = null!;

    public Guid? CastMemberId { get; set; }

    public CastMember? CastMember { get; set; }

    public Guid? PropId { get; set; }

    public Prop? Prop { get; set; }

    /// <summary>Index of the <c>@</c> in the beat's text.</summary>
    public int Offset { get; set; }

    /// <summary>Length of the reference, <c>@</c> included.</summary>
    public int Length { get; set; }
}
