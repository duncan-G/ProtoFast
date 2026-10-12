using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>The facts of a story, asked about at a moment of story time.</summary>
public sealed class FoldedGraph(IReadOnlyList<Fact> facts, IReadOnlyList<Conflict> conflicts)
{
    public IReadOnlyList<Fact> Facts { get; } = facts;

    public IReadOnlyList<Conflict> Conflicts { get; } = conflicts;

    public IEnumerable<Fact> HoldingAt(StoryTime time) => Facts.Where(f => f.HoldsAt(time));

    public IEnumerable<Fact> ObjectsIn(Guid locationId, StoryTime time) =>
        HoldingAt(time).Where(f => f.Edge == ObservationEdge.At && f.Place?.LocationId == locationId);

    public IEnumerable<Fact> StatesOf(Guid objectId, StoryTime time) =>
        HoldingAt(time).Where(f => f.Edge == ObservationEdge.State && f.ObjectId == objectId);

    public IEnumerable<Fact> ConnectionsOf(Guid locationId, StoryTime time) =>
        HoldingAt(time).Where(f => f.Edge == ObservationEdge.Connects && f.Involves(locationId));

    public bool Connected(PlaceRef one, PlaceRef other, StoryTime time) =>
        HoldingAt(time).Any(f => f.Edge == ObservationEdge.Connects
                                 && (f.Place == one && f.Other == other || f.Place == other && f.Other == one));

    /// <summary>Where each character was last placed before <paramref name="time"/>.</summary>
    public IReadOnlyDictionary<Guid, Fact> CharactersAt(StoryTime time) =>
        HoldingAt(time)
            .Where(f => f.Edge == ObservationEdge.CharacterAt && f.CharacterId is not null)
            .GroupBy(f => f.CharacterId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Start).First());

    public PlaceRef? BeatPlace(Guid locationId, StoryTime time) =>
        HoldingAt(time)
            .Where(f => f.Edge == ObservationEdge.BeatAt && f.Place?.LocationId == locationId)
            .OrderByDescending(f => f.Start)
            .FirstOrDefault()?.Place;
}
