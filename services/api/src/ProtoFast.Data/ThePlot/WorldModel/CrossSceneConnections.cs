using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>
/// The rule the story's close applies (design doc §7): when a scene follows straight on from the
/// one before (<see cref="SceneContinuity"/>) and a character last seen at one place there is first
/// seen at another, with no known way between the two locations, the two places connect. A cut in time or a
/// drive between them is no way through, and which door was used is not known, so none is given.
/// </summary>
public static class CrossSceneConnections
{
    public sealed record Connection(Guid SceneElementId, PlaceRef From, PlaceRef To);

    public static IReadOnlyList<Connection> Find(
        IReadOnlyList<ObservationRow> observations, IReadOnlyList<SceneSlot> scenes, FoldedGraph graph, IReadOnlySet<Guid> followOn)
    {
        var ordered = scenes.OrderBy(s => s.Start).ToList();
        var byScene = observations.ToLookup(o => o.SceneId);
        var found = new List<Connection>();
        var seen = new HashSet<(PlaceRef, PlaceRef)>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var next = ordered[i];
            if (!followOn.Contains(next.SceneId)
                || previous.LocationId is null || next.LocationId is null || previous.LocationId == next.LocationId)
            {
                continue;
            }

            var lastSeen = byScene[previous.SceneId]
                .Where(o => o.Edge == ObservationEdge.CharacterAt && o.CharacterId is not null)
                .GroupBy(o => o.CharacterId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Time).Last());
            var firstSeen = byScene[next.SceneId]
                .Where(o => o.Edge == ObservationEdge.CharacterAt && o.CharacterId is not null)
                .GroupBy(o => o.CharacterId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Time).First());

            foreach (var (character, arrival) in firstSeen)
            {
                if (!lastSeen.TryGetValue(character, out var departure)
                    || departure.Place is not { } from || arrival.Place is not { } to
                    || from.LocationId == to.LocationId
                    || !seen.Add((from, to)) || graph.LocationsConnected(from.LocationId, to.LocationId, next.Start))
                {
                    continue;
                }

                found.Add(new Connection(arrival.SceneElementId, from, to));
            }
        }

        return found;
    }
}
