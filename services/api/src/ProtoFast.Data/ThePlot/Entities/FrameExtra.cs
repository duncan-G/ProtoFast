namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>An extra a frame shows or hides against the scene, or stages. Owned by its <see cref="Frame"/>.</summary>
public sealed class FrameExtra
{
    public Guid FrameId { get; set; }

    public Frame Frame { get; set; } = null!;

    public Guid ExtraId { get; set; }

    public Extra Extra { get; set; } = null!;

    /// <summary>Null follows the scene's <see cref="SceneExtra.OnScreen"/>.</summary>
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

    public string Looking { get; set; } = "";

    /// <summary>For a group, which of them: "the nurse nearest the camera, mid-step, eyes on her clipboard".</summary>
    public string Pose { get; set; } = "";
}
