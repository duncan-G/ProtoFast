using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>
/// Builds facts from observations (design doc §5): each subject's observations for an edge are
/// walked in story order, and each one confirms the standing fact, changes it, or conflicts with
/// it and loses. Then the rules of §4: an occupant's facts end with its scene, and a fixture
/// reaches back to the first scene set in its place.
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
            Walk(subject.Key, subject, facts, conflicts);
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
        ObservationEdge.Connects => $"connects:{string.Join('|', Ends(o.Place!.Value, o.Other!.Value))}",
        _ => throw new ArgumentOutOfRangeException(nameof(o), o.Edge, "Unknown edge."),
    };

    private static void Walk(string key, IEnumerable<ObservationRow> subject, List<Fact> facts, List<Conflict> conflicts)
    {
        Fact? standing = null;
        foreach (var observation in subject)
        {
            if (observation.Ends)
            {
                if (standing is null)
                {
                    continue;
                }

                if (Precedence(observation) >= Precedence(standing))
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
            else if (Precedence(observation) >= Precedence(standing))
            {
                facts.Add(standing with { End = observation.Time });
                standing = incoming;
            }
            else
            {
                conflicts.Add(new Conflict(observation, standing));
            }
        }

        if (standing is not null)
        {
            facts.Add(standing);
        }
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
        _ => standing.Place == incoming.Place,
    };

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
