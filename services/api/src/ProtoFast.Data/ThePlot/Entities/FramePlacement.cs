namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A character in one frame: where they are and how they hold themselves, and what the frame
/// changes about their look in the scene. Owned by its <see cref="Frame"/>.
/// </summary>
public sealed class FramePlacement
{
    public Guid FrameId { get; set; }

    public Frame Frame { get; set; } = null!;

    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    /// <summary>Null follows the scene's <see cref="SceneAppearance.OnScreen"/>.</summary>
    public bool? OnScreen { get; set; }

    /// <summary>A <see cref="DirectionVocabulary.ScreenSides"/> label, or empty.</summary>
    public string Screen { get; set; } = "";

    /// <summary>A <see cref="DirectionVocabulary.Depths"/> label, or empty.</summary>
    public string Depth { get; set; } = "";

    /// <summary>Where in the place they are, in the place's own terms: "at the near end of the nurse station counter".</summary>
    public string Mark { get; set; } = "";

    /// <summary>A <see cref="DirectionVocabulary.Facings"/> label, or empty.</summary>
    public string Facing { get; set; } = "";

    /// <summary>A <see cref="DirectionVocabulary.Movings"/> label, or empty.</summary>
    public string Moving { get; set; } = "";

    /// <summary>Who or what they look at: "the nurse", "the doors".</summary>
    public string Looking { get; set; } = "";

    public string Pose { get; set; } = "";

    public string Expression { get; set; } = "";

    /// <summary>Null wears the scene's wardrobe.</summary>
    public Guid? WardrobeId { get; set; }

    public CharacterWardrobe? Wardrobe { get; set; }

    /// <summary>Added to the scene's wardrobe change, never replacing it.</summary>
    public string WardrobeChange { get; set; } = "";

    /// <summary>Added to the scene's appearance change, never replacing it.</summary>
    public string AppearanceChange { get; set; } = "";
}
