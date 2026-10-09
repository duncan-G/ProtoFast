namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// The state a prop is in during one scene. Owned by its <see cref="Scene"/>: it has no
/// <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class ScenePropState
{
    public Guid Id { get; set; }

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public Guid PropId { get; set; }

    public Prop Prop { get; set; } = null!;

    /// <summary>In view in every frame of the scene unless a frame says otherwise.</summary>
    public bool InView { get; set; } = true;

    /// <summary>"broken", "lit"; empty means as the sheet describes it.</summary>
    public List<string> State { get; set; } = [];

    public string? ChangeReason { get; set; }

    public Guid? ChangeElementId { get; set; }

    public SceneElement? ChangeElement { get; set; }

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }
}
