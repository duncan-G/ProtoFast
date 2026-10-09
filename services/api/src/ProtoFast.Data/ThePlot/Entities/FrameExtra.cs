namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>An extra a frame shows or hides against the scene. Owned by its <see cref="Frame"/>.</summary>
public sealed class FrameExtra
{
    public Guid FrameId { get; set; }

    public Frame Frame { get; set; } = null!;

    public Guid ExtraId { get; set; }

    public Extra Extra { get; set; } = null!;

    public bool OnScreen { get; set; } = true;
}
