using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One row in a scene's flow: a scene heading, a beat of prose or dialogue, or a transition. The
/// kinds share a table so the flow reorders as a single list; <see cref="Type"/> says which of the
/// optional columns apply, and check constraints keep the others empty.
///
/// <para>A heading opens a block: the beats after it, up to the next heading or transition, play
/// at its location, and dragging the heading moves the block. That grouping is positional and
/// derived on read, not stored.</para>
/// </summary>
public sealed class SceneElement : IDateStamped
{
    public Guid Id { get; set; }

    /// <inheritdoc cref="Story.UserId"/>
    public string UserId { get; set; } = "";

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    /// <inheritdoc cref="Act.Position"/>
    public int Position { get; set; }

    public SceneElementType Type { get; set; }

    /// <summary>
    /// The written text of an action, description, narration or dialogue beat, with library
    /// references inline as <c>@Name</c>; <see cref="Mentions"/> records where each one resolved.
    /// Empty while the beat is still blank. Null for headings and transitions.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// A heading's location, or null until one is picked. Cleared if the location is deleted, and
    /// the editor asks for a new one.
    /// </summary>
    public Guid? LocationId { get; set; }

    public Location? Location { get; set; }

    /// <summary>A heading's time of day. Required on headings, null otherwise.</summary>
    public TimeOfDay? TimeOfDay { get; set; }

    /// <summary>
    /// Who speaks a dialogue beat, or null while unassigned. Cleared if the cast member is
    /// deleted, and the editor shows the line as unassigned.
    /// </summary>
    public Guid? SpeakerId { get; set; }

    public CastMember? Speaker { get; set; }

    /// <summary>A dialogue beat's delivery note ("whispering"), without the parentheses.</summary>
    public string? Parenthetical { get; set; }

    /// <summary>A transition's cut. Required on transitions, null otherwise.</summary>
    public TransitionKind? TransitionKind { get; set; }

    public List<SceneElementMention> Mentions { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
