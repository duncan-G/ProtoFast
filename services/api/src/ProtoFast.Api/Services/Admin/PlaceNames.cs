using ProtoFast.Api.Admin.Theplot;

namespace ProtoFast.Api.Services.Admin;

/// <summary>Places as a prompt names them: a part of the place in view by its name, anything elsewhere with its location.</summary>
internal sealed class PlaceNames(GetWorldReply world, string hereLocationId)
{
    private readonly Dictionary<string, WorldLocation> _locations = world.Locations.ToDictionary(l => l.Id);
    private readonly Dictionary<string, WorldPart> _parts = world.Parts.ToDictionary(p => p.Id);

    public string Name(WorldPlace place)
    {
        var location = _locations.TryGetValue(place.LocationId, out var l) ? l.Name : "somewhere";
        if (!_parts.TryGetValue(place.PartId, out var part))
        {
            return location;
        }

        var name = part.Name.Length > 0 ? part.Name : "an unnamed part";
        return place.LocationId == hereLocationId ? name : $"{name} in {location}";
    }

    public string Described(WorldPlace place) =>
        _parts.TryGetValue(place.PartId, out var part) && part.Description.Length > 0
            ? $"{Name(place)} ({part.Description})"
            : Name(place);
}
