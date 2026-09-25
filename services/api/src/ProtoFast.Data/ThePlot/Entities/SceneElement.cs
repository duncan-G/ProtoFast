using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A row in a scene: a heading, a beat of prose or dialogue, or a transition. <see cref="Type"/>
/// decides which of the nullable columns are set.
/// </summary>
public sealed class SceneElement : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public int Position { get; set; }

    public SceneElementType Type { get; set; }

    /// <summary>Beat text. Each <c>@Name</c> in it is recorded in <see cref="Mentions"/>.</summary>
    public string? Text { get; set; }

    public Guid? LocationId { get; set; }

    public Location? Location { get; set; }

    public Guid? TimeOfDayId { get; set; }

    public TimeOfDay? TimeOfDay { get; set; }

    public Guid? SpeakerId { get; set; }

    public Character? Speaker { get; set; }

    public string? Parenthetical { get; set; }

    public Guid? TransitionId { get; set; }

    public Transition? Transition { get; set; }

    public List<SceneElementMention> Mentions { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
