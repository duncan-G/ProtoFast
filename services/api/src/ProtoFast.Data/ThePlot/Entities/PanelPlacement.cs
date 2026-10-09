namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A character in one panel: where they are and how they hold themselves, and what the panel
/// changes about their look in the scene. Owned by its <see cref="Panel"/>.
/// </summary>
public sealed class PanelPlacement
{
    public Guid PanelId { get; set; }

    public Panel Panel { get; set; } = null!;

    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    /// <summary>Null follows the scene's <see cref="SceneAppearance.OnScreen"/>.</summary>
    public bool? OnScreen { get; set; }

    /// <summary>A <see cref="DirectionVocabulary.ScreenSides"/> label, or empty.</summary>
    public string Screen { get; set; } = "";

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
