namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>A prop a panel shows or hides against the scene. Owned by its <see cref="Panel"/>.</summary>
public sealed class PanelProp
{
    public Guid PanelId { get; set; }

    public Panel Panel { get; set; } = null!;

    public Guid PropId { get; set; }

    public Prop Prop { get; set; } = null!;

    public bool InView { get; set; } = true;
}
