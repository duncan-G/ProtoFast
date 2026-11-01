using System.Text;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.Api.Services.Admin;

/// <summary>
/// A view's picture prompt: the place as the build describes it, then as it stands at the view's
/// first beat. Only what belongs to the place goes in: its description and contents, its parts,
/// its fixtures and dressing with their states, its ways out, and what changed since this camera
/// last saw it. Characters, props, occupants and the beats themselves stay out.
/// </summary>
internal static class ViewPrompt
{
    private const string Style =
        "Cinematic still from a live-action feature film: naturalistic light and colour, photographic detail, 35mm lens.";

    private const string Empty =
        "Picture the place itself as this camera sees it, before anyone steps in: an empty set. No people, animals or "
        + "crowds, and nothing anyone carries or handles; only what is built into the place or dressed on it.";

    private const string SamePlace =
        "The pictures above show this same place from other views. Keep its layout, architecture, materials and "
        + "fixtures so this one is recognisably the same place; take the framing, the light and the dressing from what follows.";

    private const string NoText = "No text, captions, subtitles, labels or watermarks anywhere in the image.";

    /// <param name="references">The views whose pictures go before the prompt.</param>
    public static string Compile(GetWorldReply world, WorldView view, IReadOnlyList<WorldView> references)
    {
        var names = new PlaceNames(world, view.Camera.LocationId);
        var locations = world.Locations.ToDictionary(l => l.Id);
        var objects = world.Objects
            .Where(o => o.Kind is WorldObjectKind.Fixture or WorldObjectKind.Dressing)
            .ToDictionary(o => o.Id);
        var moment = view.Moments.Count > 0 ? view.Moments[0] : 0;
        var holding = world.Facts.Where(f => f.Start <= moment && moment < f.End).ToList();
        var here = new HashSet<string> { view.Camera.LocationId };
        if (view.Toward is not null)
        {
            here.Add(view.Toward.LocationId);
        }

        var text = new StringBuilder();
        text.AppendLine(Style);
        text.AppendLine(Empty);
        if (references.Count > 0)
        {
            text.AppendLine(SamePlace);
        }

        text.AppendLine();
        if (locations.TryGetValue(view.Camera.LocationId, out var place))
        {
            var inside = locations.GetValueOrDefault(place.InsideLocationId);
            text.AppendLine($"Place: {place.Name}, {(place.Exterior ? "an exterior" : "an interior")}{(inside is null ? "" : $", inside {inside.Name}")}.");
        }

        if (TimeOfDay(world, view.Scene) is { Length: > 0 } time)
        {
            text.AppendLine($"Time of day: {time.ToLowerInvariant()}.");
        }

        text.AppendLine(Camera(world, view, names));
        var aim = SceneViews.Aim(world, view);
        if (aim is not null)
        {
            var framed = Capitalised(names.Name(aim));
            text.AppendLine(aim.Equals(view.Camera)
                ? $"{framed} fills the frame, not the rest of {place?.Name ?? "the place"} beyond it."
                : $"{framed} fills the frame.");
        }

        var before = SceneViews.Before(world, view);
        var turned = references.Count(r => r != before && r.Camera.Equals(view.Camera) && !Equals(SceneViews.Aim(world, r), aim));
        if (turned > 0)
        {
            text.AppendLine(turned == 1
                ? "The picture above from this same spot faces elsewhere: don't repeat its framing."
                : "The pictures above from this same spot face elsewhere: don't repeat their framing.");
        }

        if (place is { Description.Length: > 0 })
        {
            text.AppendLine();
            text.AppendLine($"What the place is: {place.Description}");
            if (place.Contents.Length > 0)
            {
                text.AppendLine($"What is in it: {place.Contents}");
            }

            text.AppendLine("That is the place as the story first finds it; where what follows differs, what follows is how it stands now.");
        }

        var parts = world.Parts.Where(p => p.LocationId == view.Camera.LocationId).ToList();
        if (parts.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Its parts:");
            foreach (var part in parts.OrderBy(p => p.HasPosition ? p.Position : int.MaxValue).ThenBy(p => p.Name, StringComparer.Ordinal))
            {
                var at = new WorldPlace { LocationId = part.LocationId, PartId = part.Id };
                text.AppendLine($"- {names.Described(at)}");
            }
        }

        var states = holding.Where(f => f.Edge == WorldEdge.State).ToLookup(f => f.ObjectId);
        var things = holding
            .Where(f => f.Edge == WorldEdge.At && f.CharacterId.Length == 0 && f.Place is not null
                        && here.Contains(f.Place.LocationId) && objects.ContainsKey(f.ObjectId))
            .Select(f => (Object: objects[f.ObjectId], f.Place))
            .OrderBy(t => t.Place.LocationId != view.Camera.LocationId)
            .ThenBy(t => names.Name(t.Place), StringComparer.Ordinal)
            .ThenBy(t => t.Object.Name, StringComparer.Ordinal)
            .ToList();
        if (things.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("What is there now:");
            foreach (var (thing, at) in things)
            {
                var state = states[thing.Id].OrderBy(s => s.Property, StringComparer.Ordinal).Select(s => $"{s.Property}: {s.Value}").ToList();
                text.AppendLine($"- {thing.Name} ({(thing.Kind == WorldObjectKind.Fixture ? "fixture" : "dressing")}"
                                + (thing.Description.Length > 0 ? $", {thing.Description}" : "")
                                + $"), at {names.Name(at)}"
                                + (state.Count > 0 ? $"; {string.Join(", ", state)}" : ""));
            }
        }

        var vias = world.Objects.ToDictionary(o => o.Id, o => o.Name);
        var ways = holding
            .Where(f => f.Edge == WorldEdge.Connects && f.Place is not null && f.Other is not null)
            .Select(f => f.Place.LocationId == view.Camera.LocationId ? (From: f.Place, To: f.Other, f.ViaObjectId)
                : f.Other.LocationId == view.Camera.LocationId ? (From: f.Other, To: f.Place, f.ViaObjectId)
                : default)
            .Where(w => w.From is not null)
            .Select(w => $"- {names.Name(w.From)} opens onto {names.Name(w.To)}"
                         + (vias.TryGetValue(w.ViaObjectId, out var via) ? $", through {via}" : ""))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        if (ways.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Ways out:");
            foreach (var way in ways)
            {
                text.AppendLine(way);
            }
        }

        if (before is not null)
        {
            var places = here.ToHashSet();
            var since = PlaceChanges.Of(world)
                .Where(c => c.Moment > before.Moments[^1] && c.Moment <= moment && c.Locations.Any(places.Contains))
                .Select(c => c.Text)
                .Distinct()
                .ToList();
            if (since.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Changed since this camera last saw the place:");
                foreach (var change in since)
                {
                    text.AppendLine($"- {change}");
                }
            }
        }

        text.AppendLine();
        text.Append(NoText);
        return text.ToString();
    }

    /// <summary>Sent before a reference picture of another view of the same place.</summary>
    /// <param name="earlier">It is the same camera, before the place changed.</param>
    public static string Caption(GetWorldReply world, WorldView view, bool earlier = false)
    {
        var names = new PlaceNames(world, view.Camera.LocationId);
        var location = world.Locations.FirstOrDefault(l => l.Id == view.Camera.LocationId)?.Name ?? "this place";
        var time = TimeOfDay(world, view.Scene);
        var caption = $"{location} in scene {view.Scene + 1}{(time.Length > 0 ? $" ({time.ToLowerInvariant()})" : "")}: {Camera(world, view, names)}";
        return earlier ? $"{caption} This is the same camera before the changes listed below." : caption;
    }

    private static string Camera(GetWorldReply world, WorldView view, PlaceNames names)
    {
        var stands = view.Camera.PartId.Length > 0 ? $"stands at {names.Described(view.Camera)}" : $"stands in {names.Name(view.Camera)}";
        var aim = SceneViews.Aim(world, view);
        return aim is null ? $"The camera {stands}."
            : aim.Equals(view.Camera) ? $"The camera {stands}, looking at {names.Name(aim)} close up."
            : $"The camera {stands}, looking toward {names.Described(aim)}.";
    }

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>A scene that follows straight on keeps the light of the scene it follows.</summary>
    private static string TimeOfDay(GetWorldReply world, int scene)
    {
        for (var index = scene; index >= 0; index--)
        {
            var time = world.Scenes[index].TimeOfDay.Trim();
            if (!SceneContinuity.FollowsOn(time))
            {
                return time;
            }
        }

        return "";
    }
}
