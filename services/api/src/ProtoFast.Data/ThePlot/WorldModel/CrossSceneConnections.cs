using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>
/// The rule the story's close applies (design doc §7): a character last seen at one place in a
/// scene and first seen at another in the next scene, with no known way between them, gives a
/// <see cref="ObservationEdge.Connects"/> between the two, through whatever way out the first
/// scene showed at that place.
/// </summary>
public static class CrossSceneConnections
{
    public sealed record Connection(Guid SceneElementId, PlaceRef From, PlaceRef To, Guid? ViaObjectId);

    public static IReadOnlyList<Connection> Find(
        IReadOnlyList<ObservationRow> observations, IReadOnlyList<SceneSlot> scenes, FoldedGraph graph)
    {
        var ordered = scenes.OrderBy(s => s.Start).ToList();
        var byScene = observations.ToLookup(o => o.SceneId);
        var found = new List<Connection>();
        var seen = new HashSet<(PlaceRef, PlaceRef)>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var next = ordered[i];
            if (previous.LocationId is null || next.LocationId is null || previous.LocationId == next.LocationId)
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
                    || !seen.Add((from, to)) || graph.Connected(from, to, next.Start))
                {
                    continue;
                }

                var wayOut = byScene[previous.SceneId]
                    .Where(o => o.Edge == ObservationEdge.Connects && o.ViaObjectId is not null
                                && (o.Place == from || o.Other == from))
                    .OrderBy(o => o.Time)
                    .LastOrDefault();
                found.Add(new Connection(arrival.SceneElementId, from, to, wayOut?.ViaObjectId));
            }
        }

        return found;
    }
}
