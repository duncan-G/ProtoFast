using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>The graph's rows as they stand, loaded afresh before each scene is read.</summary>
public sealed record GraphSnapshot(
    IReadOnlyList<LocationInfo> Locations,
    IReadOnlyList<PartInfo> Parts,
    IReadOnlyList<ObjectInfo> Objects,
    IReadOnlyList<ObservationRow> Observations);
