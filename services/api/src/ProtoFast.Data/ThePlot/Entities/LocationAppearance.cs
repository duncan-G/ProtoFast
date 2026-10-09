namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>What a location looks like. Stored as JSON on the location.</summary>
public sealed class LocationAppearance
{
    public string Layout { get; set; } = "";

    public string KeyFeatures { get; set; } = "";

    public List<LocationLighting> Lighting { get; set; } = [];
}
