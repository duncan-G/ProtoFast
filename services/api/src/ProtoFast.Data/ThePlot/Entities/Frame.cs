namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One picture of the scene, covering a contiguous run of its elements. Owned by its
/// <see cref="Scene"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class Frame
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

    /// <summary>Where the camera stands in the place and what it looks toward: "inside the corridor, looking back at the doors".</summary>
    public string Camera { get; set; } = "";

    /// <summary>What this frame adds to the location's look in the scene: "crane silhouetted".</summary>
    public string LocationChange { get; set; } = "";

    /// <summary>Only for frames with no dialogue to time them.</summary>
    public int? HoldMs { get; set; }

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }

    /// <summary>The author is done with it: no directing pass or picture generation touches it.</summary>
    public bool Locked { get; set; }

    /// <summary>Only the characters this frame says something about; the rest follow the scene.</summary>
    public List<FramePlacement> Placements { get; set; } = [];

    /// <summary>Only the props this frame shows or hides against the scene.</summary>
    public List<FrameProp> Props { get; set; } = [];

    /// <summary>Only the extras this frame shows or hides against the scene.</summary>
    public List<FrameExtra> Extras { get; set; } = [];

    /// <summary>The frame pictured, if generated; stale once what it shows changed.</summary>
    public Guid? ImageAssetId { get; set; }

    public MediaAsset? Image { get; set; }
}
