using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One numbered revision of a story's screenplay ("Draft 3" in the editor's breadcrumb). Drafts
/// hold their own acts and scenes but share the story's library, so a character renamed in one
/// draft is renamed in all of them.
/// </summary>
public sealed class Draft : IDateStamped
{
    public Guid Id { get; set; }

    /// <inheritdoc cref="Story.UserId"/>
    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    /// <summary>1-based and unique within the story; the editor shows it as "Draft {Number}".</summary>
    public int Number { get; set; }

    /// <summary>An optional label the writer gives the revision ("Table read notes").</summary>
    public string? Name { get; set; }

    public List<Act> Acts { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
