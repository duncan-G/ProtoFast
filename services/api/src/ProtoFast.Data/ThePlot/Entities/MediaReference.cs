namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>An image another was composed from. Stored as JSON on the <see cref="MediaAsset"/>.</summary>
public sealed class MediaReference
{
    public Guid AssetId { get; set; }

    /// <summary>The asset's bytes when used; a library picture rendered again since makes the composition stale.</summary>
    public string ContentHash { get; set; } = "";

    /// <summary>character, location, prop or frame.</summary>
    public string Source { get; set; } = "";

    /// <summary>The character, location, prop or frame it pictures.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>What it shows, as the author reads it: "MARA", "Frame 3", "Harbour, scene 2".</summary>
    public string Label { get; set; } = "";
}
