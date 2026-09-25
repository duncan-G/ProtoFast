using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>An ordered act within a story ("Act I" in the editor's breadcrumb).</summary>
public sealed class Act : IDateStamped
{
    public Guid Id { get; set; }

    /// <inheritdoc cref="Story.UserId"/>
    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    /// <summary>
    /// 0-based order within the story. Not unique, so a reorder can rewrite positions in any
    /// order inside one save; readers order by it and break ties by id.
    /// </summary>
    public int Position { get; set; }

    /// <summary>The writer's title, or null to show the numeral the position implies ("Act I").</summary>
    public string? Title { get; set; }

    public List<Scene> Scenes { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
