namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One <c>@Name</c> reference inside a beat's <see cref="SceneElement.Text"/>, resolved to the
/// cast member or prop it names. Written alongside the text: saving a beat replaces its mentions.
///
/// <para>Stored rather than re-parsed on read so the library can answer "where is this used"
/// (the per-character mention counts, "Props in play", unused props) without scanning prose, and
/// so a rename can rewrite each reference at its recorded span.</para>
/// </summary>
public sealed class SceneElementMention
{
    public Guid Id { get; set; }

    /// <inheritdoc cref="Story.UserId"/>
    public string UserId { get; set; } = "";

    public Guid SceneElementId { get; set; }

    public SceneElement SceneElement { get; set; } = null!;

    /// <summary>Set when the mention names a cast member; exactly one of this and <see cref="PropId"/> is.</summary>
    public Guid? CastMemberId { get; set; }

    public CastMember? CastMember { get; set; }

    /// <summary>Set when the mention names a prop; exactly one of this and <see cref="CastMemberId"/> is.</summary>
    public Guid? PropId { get; set; }

    public Prop? Prop { get; set; }

    /// <summary>The index of the <c>@</c> in the beat's text.</summary>
    public int Offset { get; set; }

    /// <summary>The span's length, <c>@</c> included.</summary>
    public int Length { get; set; }
}
