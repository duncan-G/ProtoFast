namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// An extra in one scene. Owned by its <see cref="Scene"/>: it has no <c>UserId</c> and is only
/// read and written through it.
/// </summary>
public sealed class SceneExtra
{
    public Guid Id { get; set; }

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public Guid ExtraId { get; set; }

    public Extra Extra { get; set; } = null!;

    /// <summary>On screen in every frame of the scene unless a frame says otherwise.</summary>
    public bool OnScreen { get; set; } = true;

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }
}
