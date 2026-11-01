using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>
/// An interior location a beat walks into straight from another interior is in the same
/// building: the landing a flat opens onto is in the flat's building too, and a room entered
/// straight from inside a ship that already holds rooms is one of the ship's.
/// </summary>
public static class LocationNesting
{
    /// <summary>Nests every interior joined to a nested interior by a read connection; returns how many it nested.</summary>
    public static int Spread(IReadOnlyList<Location> locations, IEnumerable<ObservationRow> observations)
    {
        var byId = locations.ToDictionary(l => l.Id);
        var pairs = observations
            .Where(o => o is { Edge: ObservationEdge.Connects, Origin: ObservationOrigin.Extracted, OtherLocationId: not null }
                        && o.PlaceLocationId != o.OtherLocationId && !o.Ends)
            .Select(o => (One: o.PlaceLocationId!.Value, Other: o.OtherLocationId!.Value))
            .ToList();
        var nested = 0;
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (one, other) in pairs.Concat(pairs.Select(p => (One: p.Other, Other: p.One))))
            {
                if (!byId.TryGetValue(one, out var inner) || !byId.TryGetValue(other, out var neighbour)
                    || inner.InsideLocationId is not null
                    || inner.Setting != LocationSetting.Interior || neighbour.Setting != LocationSetting.Interior)
                {
                    continue;
                }

                var outer = neighbour.InsideLocationId
                            ?? (locations.Any(l => l.InsideLocationId == neighbour.Id) ? neighbour.Id : (Guid?)null);
                if (outer is null || outer == inner.Id || Within(byId, outer.Value, inner.Id))
                {
                    continue;
                }

                inner.InsideLocationId = outer.Value;
                nested++;
                changed = true;
            }
        }

        return nested;
    }

    private static bool Within(IReadOnlyDictionary<Guid, Location> byId, Guid location, Guid container)
    {
        for (var at = byId.GetValueOrDefault(location); at?.InsideLocationId is { } up; at = byId.GetValueOrDefault(up))
        {
            if (up == container)
            {
                return true;
            }
        }

        return false;
    }
}
