using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>
/// One edge holding over a stretch of story time, folded from the observations that say so.
/// <see cref="Place"/> is where the subject is; for a connection it is one end and
/// <see cref="Other"/> the other, in canonical order.
/// </summary>
public sealed record Fact(
    ObservationEdge Edge,
    string Key,
    Guid? ObjectId,
    Guid? CharacterId,
    PlaceRef? Place,
    PlaceRef? Other,
    Guid? ViaObjectId,
    string? Property,
    string? Value,
    ObservationBasis Basis,
    ObservationOrigin Origin,
    Guid SceneId,
    StoryTime Start,
    StoryTime? End,
    Guid ObservationId)
{
    public bool HoldsAt(StoryTime time) => Start <= time && (End is null || time < End.Value);

    public bool Involves(Guid locationId) => Place?.LocationId == locationId || Other?.LocationId == locationId;
}
