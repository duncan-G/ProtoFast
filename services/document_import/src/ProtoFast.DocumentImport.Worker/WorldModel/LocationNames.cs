using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>
/// How place names meet the story's locations: a heading that nests names ("HOSPITAL – TRAUMA
/// WING") is inside the outer one, a part named like a location is that location ("the lobby"
/// of a HOSPITAL scene when the story has LOBBY), and one named after it is a part of it ("lobby: counter").
/// </summary>
public static class LocationNames
{
    private static readonly string[] NestingSeparators = [" – ", " — ", " - ", " / "];

    /// <summary>"HOSPITAL / TRAUMA WING" is ("HOSPITAL", "TRAUMA WING"); a name that does not nest is null.</summary>
    public static (string Outer, string Inner)? Split(string name)
    {
        var (index, separator) = NestingSeparators
            .Select(s => (Index: name.LastIndexOf(s, StringComparison.Ordinal), Separator: s))
            .MaxBy(s => s.Index);
        return index > 0 ? (name[..index].Trim(), name[(index + separator.Length)..].Trim()) : null;
    }

    /// <summary>
    /// The location a part of <paramref name="locationId"/> called <paramref name="name"/> really is:
    /// another location with that name, else one nested in it whose own name is that.
    /// </summary>
    public static Guid? Shadowed(string? name, Guid locationId, IEnumerable<Location> locations)
    {
        if (Normalize(name) is not { } wanted)
        {
            return null;
        }

        var others = locations.Where(l => l.Id != locationId).ToList();
        var named = others.Where(l => Normalize(l.Name) == wanted).ToList();
        if (named.Count == 1)
        {
            return named[0].Id;
        }

        var nested = others
            .Where(l => l.InsideLocationId == locationId && Split(l.Name) is { } split && Normalize(split.Inner) == wanted)
            .ToList();
        return nested.Count == 1 ? nested[0].Id : null;
    }

    /// <summary>
    /// A part named after a location and a position in it ("lobby: counter") is that position of
    /// that location: (LOBBY, "counter"). Null when the name does not start with a location's.
    /// </summary>
    public static (Guid LocationId, string Name)? Prefixed(string? name, Guid locationId, IEnumerable<Location> locations)
    {
        var colon = name?.IndexOf(':') ?? -1;
        if (colon <= 0 || name![(colon + 1)..].Trim() is not { Length: > 0 } rest)
        {
            return null;
        }

        var prefix = name[..colon];
        var all = locations.ToList();
        if (Shadowed(prefix, locationId, all) is { } other)
        {
            return (other, rest);
        }

        var here = all.FirstOrDefault(l => l.Id == locationId);
        return here is not null && Normalize(here.Name) == Normalize(prefix) ? (locationId, rest) : null;
    }

    private static string? Normalize(string? name)
    {
        var words = (name ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var from = words is ["the", _, ..] ? 1 : 0;
        return words.Length > from ? string.Join(' ', words[from..]) : null;
    }
}
