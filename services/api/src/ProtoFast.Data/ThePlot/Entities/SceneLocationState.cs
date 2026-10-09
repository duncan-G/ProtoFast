namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// What is different about a location in one scene ("fog rolling in"), on top of its sheet. Owned
/// by its <see cref="Scene"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class SceneLocationState
{
    public Guid Id { get; set; }

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public Guid LocationId { get; set; }

    public Location Location { get; set; } = null!;

    public string Change { get; set; } = "";

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }
}
