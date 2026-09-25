using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class Scene : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid ContainerId { get; set; }

    public Container Container { get; set; } = null!;

    public int Position { get; set; }

    public required string Title { get; set; }

    public List<SceneElement> Elements { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
