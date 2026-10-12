using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>An observation joined to its beat's position, which is all the fold needs of it.</summary>
public sealed record ObservationRow(
    Guid Id,
    Guid SceneElementId,
    Guid SceneId,
    StoryTime Time,
    ObservationEdge Edge,
    Guid? ObjectId,
    Guid? CharacterId,
    Guid? PlaceLocationId,
    Guid? PlacePartId,
    Guid? OtherLocationId,
    Guid? OtherPartId,
    Guid? ViaObjectId,
    string? Property,
    string? Value,
    ObservationBasis Basis,
    ObservationOrigin Origin)
{
    public PlaceRef? Place => PlaceLocationId is { } location ? new PlaceRef(location, PlacePartId) : null;

    public PlaceRef? Other => OtherLocationId is { } location ? new PlaceRef(location, OtherPartId) : null;

    /// <summary>An object that left, or a connection that closed.</summary>
    public bool Ends =>
        Edge == ObservationEdge.At && Place is null
        || Edge == ObservationEdge.Connects && string.Equals(Value, "closed", StringComparison.OrdinalIgnoreCase);
}
