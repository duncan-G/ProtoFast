using ProtoFast.Api.Admin.Theplot;

namespace ProtoFast.Api.Services.Admin;

/// <summary>
/// The physical changes to the world's places: a fixture or dressing moved, gone or in a new
/// state, dressing that appears, a way that changes or closes. Characters, props and
/// occupants are action, not a change to a place. A first mention of anything else says how the
/// place was all along, so it is no change.
/// </summary>
internal static class PlaceChanges
{
    public static List<PlaceChange> Of(GetWorldReply world)
    {
        var objects = world.Objects.ToDictionary(o => o.Id);
        bool OfTheSet(WorldFact f) =>
            f.CharacterId.Length == 0 && objects.TryGetValue(f.ObjectId, out var o) && o.Kind is WorldObjectKind.Fixture or WorldObjectKind.Dressing;
        var homes = world.Facts
            .Where(f => f.Edge == WorldEdge.At && f.Place is not null && OfTheSet(f))
            .ToLookup(f => f.ObjectId, f => f.Place.LocationId);
        var end = world.Scenes.Sum(s => 1 + s.Beats.Count);

        var changes = new List<PlaceChange>();
        foreach (var subject in world.Facts.Where(f => f.Start < f.End).GroupBy(f => f.Key))
        {
            var run = subject.OrderBy(f => f.Start).ToList();
            var first = run[0];
            string[] places = first.Edge switch
            {
                WorldEdge.At when first.Place is not null && OfTheSet(first) => run.Select(f => f.Place.LocationId).Distinct().ToArray(),
                WorldEdge.State when OfTheSet(first) => homes[first.ObjectId].Distinct().ToArray(),
                WorldEdge.Connects when first.Place is not null && first.Other is not null =>
                    new[] { first.Place.LocationId, first.Other.LocationId }.Distinct().ToArray(),
                _ => [],
            };
            if (places.Length == 0)
            {
                continue;
            }

            var names = new PlaceNames(world, places[0]);
            var name = objects.TryGetValue(first.ObjectId, out var thing) ? thing.Name : "";
            var appears = first.Edge == WorldEdge.At && thing?.Kind == WorldObjectKind.Dressing;
            for (var i = appears ? 0 : 1; i < run.Count; i++)
            {
                var fact = run[i];
                changes.Add(new PlaceChange(fact.Start, places, fact.Edge switch
                {
                    WorldEdge.At => $"{name} is now at {names.Name(fact.Place)}",
                    WorldEdge.State => $"{name}: {fact.Property} is now {fact.Value}",
                    _ => $"{names.Name(fact.Place)} now opens onto {names.Name(fact.Other)}"
                         + (objects.TryGetValue(fact.ViaObjectId, out var via) ? $" through {via.Name}" : ""),
                }));
            }

            var last = run[^1];
            if (last.End < end && last.Edge != WorldEdge.State)
            {
                changes.Add(new PlaceChange(last.End, places, last.Edge == WorldEdge.At
                    ? $"{name} is gone"
                    : $"{names.Name(last.Place)} no longer opens onto {names.Name(last.Other)}"));
            }
        }

        return changes.OrderBy(c => c.Moment).ToList();
    }
}
