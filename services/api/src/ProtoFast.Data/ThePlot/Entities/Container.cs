using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class Container : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public int Position { get; set; }

    /// <summary>What the editor shows for the container: "Act I", "Part One", "Episode 3".</summary>
    public required string Label { get; set; }

    public List<Scene> Scenes { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
