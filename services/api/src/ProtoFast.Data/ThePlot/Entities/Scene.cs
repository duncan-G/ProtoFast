using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One scene in the scene editor: a title and the ordered <see cref="SceneElement"/>s the writer
/// lays out under it. The editor's "SCENE 4 / 5" is the scene's place among its act's scenes.
/// </summary>
public sealed class Scene : IDateStamped
{
    public Guid Id { get; set; }

    public Guid ActId { get; set; }

    public Act Act { get; set; } = null!;

    /// <inheritdoc cref="Act.Position"/>
    public int Position { get; set; }

    /// <summary>The header title; a new scene starts as "Untitled scene".</summary>
    public required string Title { get; set; }

    public List<SceneElement> Elements { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
