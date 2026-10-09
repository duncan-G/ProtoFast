namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>A prop a frame shows or hides against the scene. Owned by its <see cref="Frame"/>.</summary>
public sealed class FrameProp
{
    public Guid FrameId { get; set; }

    public Frame Frame { get; set; } = null!;

    public Guid PropId { get; set; }

    public Prop Prop { get; set; } = null!;

    public bool InView { get; set; } = true;
}
