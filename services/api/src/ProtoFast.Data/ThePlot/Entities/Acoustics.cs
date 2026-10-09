namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>How a location sounds. Stored as JSON on the location.</summary>
public sealed class Acoustics
{
    /// <summary>"small tiled kitchen"</summary>
    public string Space { get; set; } = "";

    /// <summary>"fridge hum, distant traffic"</summary>
    public string Ambience { get; set; } = "";
}
