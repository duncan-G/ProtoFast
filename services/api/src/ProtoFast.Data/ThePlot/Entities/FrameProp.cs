namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>A prop a frame shows or hides against the scene, or places. Owned by its <see cref="Frame"/>.</summary>
public sealed class FrameProp
{
    public Guid FrameId { get; set; }

    public Frame Frame { get; set; } = null!;

    public Guid PropId { get; set; }

    public Prop Prop { get; set; } = null!;

    public bool InView { get; set; } = true;

    /// <summary>A <see cref="DirectionVocabulary.ScreenSides"/> label, or empty; a carried prop goes where its holder is.</summary>
    public string Screen { get; set; } = "";

    /// <summary>A <see cref="DirectionVocabulary.Depths"/> label, or empty.</summary>
    public string Depth { get; set; } = "";

    /// <summary>Where in the place it is: "on the counter beside the phone".</summary>
    public string Mark { get; set; } = "";
}
