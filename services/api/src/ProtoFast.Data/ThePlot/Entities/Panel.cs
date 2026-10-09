namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One picture of the scene, covering a contiguous run of its elements. Owned by its
/// <see cref="Scene"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class Panel
{
    public Guid Id { get; set; }

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public int Position { get; set; }

    public Guid FirstElementId { get; set; }

    public SceneElement FirstElement { get; set; } = null!;

    public Guid LastElementId { get; set; }

    public SceneElement LastElement { get; set; } = null!;

    public Guid? LocationId { get; set; }

    public Location? Location { get; set; }

    public string? TimeOfDay { get; set; }

    /// <summary>What is happening: an action, or "MARA presses, TOM deflects".</summary>
    public string Beat { get; set; } = "";

    /// <summary>A <see cref="DirectionVocabulary.Shots"/> label; null takes the scene's.</summary>
    public string? Shot { get; set; }

    /// <summary>A <see cref="DirectionVocabulary.Angles"/> label; null takes the scene's.</summary>
    public string? Angle { get; set; }

    /// <summary>A <see cref="DirectionVocabulary.Movements"/> label; null takes the scene's.</summary>
    public string? Movement { get; set; }

    /// <summary>What this panel adds to the location's look in the scene: "crane silhouetted".</summary>
    public string LocationChange { get; set; } = "";

    /// <summary>Only for panels with no dialogue to time them.</summary>
    public int? HoldMs { get; set; }

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }

    /// <summary>Only the characters this panel says something about; the rest follow the scene.</summary>
    public List<PanelPlacement> Placements { get; set; } = [];

    /// <summary>Only the props this panel shows or hides against the scene.</summary>
    public List<PanelProp> Props { get; set; } = [];
}
