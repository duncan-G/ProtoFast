using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>
/// Builds facts from observations (design doc §5): each subject's observations for an edge are
/// walked in story order, and each one confirms the standing fact, changes it, or conflicts with
/// it and loses. A later beat explains a change, so only a fixture's place, or two observations
/// of the same beat, can conflict. Then the rules of §4: an occupant's facts end with its scene, and a fixture
/// reaches back to the first scene set in its place. A camera is set per beat, so each one
/// replaces the last; a scene's cameras span the whole scene and end with it. A held object
/// is put down where it is when its scene ends; a later scene picks it up again by saying so.
/// </summary>
public static class WorldModelFolder
{
    public static FoldedGraph Fold(
        IReadOnlyList<ObservationRow> observations,
        IReadOnlyList<SceneSlot> scenes,
        IReadOnlyDictionary<Guid, ObjectKind> kinds)
    {
        var facts = new List<Fact>();
        var conflicts = new List<Conflict>();
        foreach (var subject in observations.OrderBy(o => o.Time).GroupBy(KeyOf))
        {
            Walk(subject.Key, subject, kinds, facts, conflicts);
        }

        var scenesById = scenes.ToDictionary(s => s.SceneId);
        for (var i = 0; i < facts.Count; i++)
        {
            var fact = facts[i];
            if (fact.Edge != ObservationEdge.At || fact.ObjectId is not { } objectId || !kinds.TryGetValue(objectId, out var kind))
            {
                continue;
            }

            if (kind == ObjectKind.Occupant && scenesById.TryGetValue(fact.SceneId, out var scene)
                && (fact.End is null || scene.End < fact.End.Value))
            {
                facts[i] = fact with { End = scene.End };
            }
        }

        foreach (var shots in Enumerable.Range(0, facts.Count)
                     .Where(i => facts[i].Edge == ObservationEdge.Camera)
                     .GroupBy(i => facts[i].SceneId))
        {
            if (!scenesById.TryGetValue(shots.Key, out var scene))
            {
                continue;
            }

            var first = shots.MinBy(i => facts[i].Start);
            var last = shots.MaxBy(i => facts[i].Start);
            facts[first] = facts[first] with { Start = scene.Start };
            facts[last] = facts[last] with { End = scene.End };
        }

        foreach (var first in facts
                     .Where(f => f.Edge == ObservationEdge.At && f.ObjectId is { } id && kinds.GetValueOrDefault(id) == ObjectKind.Fixture)
                     .GroupBy(f => f.ObjectId!.Value)
                     .Select(g => g.OrderBy(f => f.Start).First()))
        {
            var earliest = scenes
                .Where(s => s.LocationId == first.Place?.LocationId)
                .Select(s => s.Start)
                .DefaultIfEmpty(first.Start)
                .Min();
            if (earliest < first.Start)
            {
                facts[facts.IndexOf(first)] = first with { Start = earliest };
            }
        }

        return new FoldedGraph(facts.OrderBy(f => f.Start).ToList(), conflicts);
    }

    /// <summary>The subject-and-edge a fact is about; two observations with the same key compete.</summary>
    public static string KeyOf(ObservationRow o) => o.Edge switch
    {
        ObservationEdge.At => $"at:{o.ObjectId}",
        ObservationEdge.State => $"state:{o.ObjectId}:{o.Property?.Trim().ToLowerInvariant()}",
        ObservationEdge.CharacterAt => $"character:{o.CharacterId}",
        ObservationEdge.BeatAt => $"beat:{o.PlaceLocationId}",
        ObservationEdge.Camera => $"camera:{o.SceneId}",
        ObservationEdge.Connects => $"connects:{string.Join('|', Ends(o.Place!.Value, o.Other!.Value))}",
        _ => throw new ArgumentOutOfRangeException(nameof(o), o.Edge, "Unknown edge."),
    };

    private static void Walk(
        string key, IEnumerable<ObservationRow> subject, IReadOnlyDictionary<Guid, ObjectKind> kinds, List<Fact> facts, List<Conflict> conflicts)
    {
        Fact? standing = null;
        foreach (var observation in subject)
        {
            if (standing is { CharacterId: not null, Edge: ObservationEdge.At } && observation.Time > SceneEndOf(standing))
            {
                standing = PutDown(standing, facts);
            }

            if (observation.Ends)
            {
                if (standing is null)
                {
                    continue;
                }

                if (Replaces(observation, standing, kinds))
                {
                    facts.Add(standing with { End = observation.Time });
                    standing = null;
                }
                else
                {
                    conflicts.Add(new Conflict(observation, standing));
                }

                continue;
            }

            var incoming = FactOf(key, observation);
            if (standing is null)
            {
                standing = incoming;
            }
            else if (SameValue(standing, incoming))
            {
                // Confirms.
            }
            else if (Replaces(observation, standing, kinds))
            {
                facts.Add(standing with { End = observation.Time });
                standing = incoming;
            }
            else
            {
                conflicts.Add(new Conflict(observation, standing));
            }
        }

        if (standing is { CharacterId: not null, Edge: ObservationEdge.At })
        {
            standing = PutDown(standing, facts);
        }

        if (standing is not null)
        {
            facts.Add(standing);
        }
    }

    private static StoryTime SceneEndOf(Fact fact) => StoryTime.SceneEnd(fact.Start.Container, fact.Start.Scene);

    private static Fact PutDown(Fact held, List<Fact> facts)
    {
        var end = SceneEndOf(held);
        facts.Add(held with { End = end });
        return held with { CharacterId = null, Start = end };
    }

    private static Fact FactOf(string key, ObservationRow o)
    {
        var (place, other) = o.Edge == ObservationEdge.Connects
            ? Ordered(o.Place!.Value, o.Other!.Value)
            : (o.Place, o.Other);
        return new Fact(
            o.Edge, key, o.ObjectId, o.CharacterId, place, other, o.ViaObjectId, o.Property, o.Value,
            o.Basis, o.Origin, o.SceneId, o.Time, End: null, o.Id);
    }

    private static bool SameValue(Fact standing, Fact incoming) => standing.Edge switch
    {
        ObservationEdge.State => string.Equals(standing.Value?.Trim(), incoming.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
        ObservationEdge.Connects => standing.ViaObjectId == incoming.ViaObjectId || incoming.ViaObjectId is null,
        ObservationEdge.Camera => standing.Place == incoming.Place && standing.Other == incoming.Other,
        ObservationEdge.At => standing.Place == incoming.Place && standing.CharacterId == incoming.CharacterId,
        _ => standing.Place == incoming.Place,
    };

    /// <summary>
    /// A camera is chosen per beat and anything but a fixture can move between beats, so those
    /// replace the standing fact; otherwise the higher-ranked observation stands.
    /// </summary>
    private static bool Replaces(ObservationRow observation, Fact standing, IReadOnlyDictionary<Guid, ObjectKind> kinds)
    {
        if (observation.Edge == ObservationEdge.Camera)
        {
            return true;
        }

        var fixture = observation.Edge == ObservationEdge.At && observation.ObjectId is { } id && kinds.GetValueOrDefault(id) == ObjectKind.Fixture;
        return observation.Time > standing.Start && !fixture || Precedence(observation) >= Precedence(standing);
    }

    /// <summary>An author's word, then a stated observation, then an implied one.</summary>
    private static int Precedence(ObservationRow o) => Precedence(o.Origin, o.Basis);

    private static int Precedence(Fact f) => Precedence(f.Origin, f.Basis);

    private static int Precedence(ObservationOrigin origin, ObservationBasis basis) =>
        origin == ObservationOrigin.Author ? 3 : basis == ObservationBasis.Stated ? 2 : 1;

    private static (PlaceRef?, PlaceRef?) Ordered(PlaceRef one, PlaceRef other)
    {
        var ends = Ends(one, other);
        return (ends[0], ends[1]);
    }

    private static PlaceRef[] Ends(PlaceRef one, PlaceRef other) =>
        string.CompareOrdinal(one.ToString(), other.ToString()) <= 0 ? [one, other] : [other, one];
}
