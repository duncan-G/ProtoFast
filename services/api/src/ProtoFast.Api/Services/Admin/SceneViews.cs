using ProtoFast.Api.Admin.Theplot;

namespace ProtoFast.Api.Services.Admin;

/// <summary>
/// A scene's views: each place its camera stands with the place it looks toward, and the beats
/// filmed from there. A shot and its reverse are two views, however often the scene cuts between
/// them; a physical change to the place between two beats from the same camera starts another.
/// </summary>
internal static class SceneViews
{
    public static List<WorldView> Of(GetWorldReply world)
    {
        var cameras = world.Facts
            .Where(f => f.Edge == WorldEdge.Camera && f.Place is not null && f.Start < f.End)
            .OrderBy(f => f.Start)
            .ToList();
        var changes = PlaceChanges.Of(world);
        var views = new List<WorldView>();
        var sceneStart = 0;
        for (var index = 0; index < world.Scenes.Count; index++)
        {
            var sceneEnd = sceneStart + 1 + world.Scenes[index].Beats.Count;
            var shots = cameras.Where(f => f.Start < sceneEnd && f.End > sceneStart).ToList();
            var open = new Dictionary<(WorldPlace, WorldPlace?), (WorldView View, int Last)>();
            for (var moment = sceneStart + 1; moment < sceneEnd; moment++)
            {
                var shot = shots.LastOrDefault(f => f.Start <= moment && moment < f.End);
                if (shot is null)
                {
                    continue;
                }

                var view = open.TryGetValue((shot.Place, shot.Other), out var standing) ? standing.View : null;
                if (view is null || Changed(changes, shot, standing.Last, moment))
                {
                    view = new WorldView
                    {
                        Scene = index, Camera = shot.Place.Clone(), Toward = shot.Other?.Clone(), State = view is null ? 0 : view.State + 1,
                    };
                    views.Add(view);
                }

                view.Moments.Add(moment);
                open[(shot.Place, shot.Other)] = (view, moment);
            }

            sceneStart = sceneEnd;
        }

        return views;
    }

    public static ViewKey KeyOf(GetWorldReply world, WorldView view, IReadOnlyDictionary<string, WorldPart> parts) => new(
        Guid.Parse(world.Scenes[view.Scene].Id),
        Guid.Parse(view.Camera.LocationId),
        PartPath(parts, view.Camera.PartId),
        view.Toward is null ? null : Guid.Parse(view.Toward.LocationId),
        view.Toward is null ? null : PartPath(parts, view.Toward.PartId),
        view.State);

    /// <summary>Finds the scene's view with both ends; null when the world has no such view.</summary>
    public static WorldView? Find(GetWorldReply world, string sceneId, WorldPlace? camera, WorldPlace? toward, int state)
    {
        var scene = Enumerable.Range(0, world.Scenes.Count).FirstOrDefault(i => world.Scenes[i].Id == sceneId, -1);
        return scene < 0 || camera is null
            ? null
            : world.Views.FirstOrDefault(v => v.Scene == scene && v.Camera.Equals(camera) && Equals(v.Toward, toward) && v.State == state);
    }

    /// <summary>The same camera in the same scene, before the place last changed; null for a view's first look.</summary>
    public static WorldView? Before(GetWorldReply world, WorldView view) =>
        view.State == 0
            ? null
            : world.Views.FirstOrDefault(v => v.Scene == view.Scene && v.Camera.Equals(view.Camera) && Equals(v.Toward, view.Toward)
                                              && v.State == view.State - 1);

    /// <summary>
    /// Where the view looks: its toward place, or else the part of the camera's place its beats
    /// mostly play in. Null when neither says more than the camera's location.
    /// </summary>
    public static WorldPlace? Aim(GetWorldReply world, WorldView view)
    {
        if (view.Toward is not null)
        {
            return view.Toward;
        }

        var location = view.Camera.LocationId;
        return view.Moments
            .Select(m => world.Facts.FirstOrDefault(f => f.Edge == WorldEdge.BeatAt && f.Place?.LocationId == location
                                                         && f.Start <= m && m < f.End)?.Place)
            .OfType<WorldPlace>()
            .Where(p => p.PartId.Length > 0)
            .GroupBy(p => p)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static bool Changed(List<PlaceChange> changes, WorldFact shot, int after, int upTo) =>
        changes.Any(c => c.Moment > after && c.Moment <= upTo
                         && (c.Locations.Contains(shot.Place.LocationId) || shot.Other is not null && c.Locations.Contains(shot.Other.LocationId)));

    /// <summary>A part's name after its parents'; an unnamed part goes by its description.</summary>
    private static string? PartPath(IReadOnlyDictionary<string, WorldPart> parts, string partId)
    {
        if (partId.Length == 0 || !parts.TryGetValue(partId, out var part))
        {
            return null;
        }

        var name = part.Name.Length > 0 ? part.Name : part.Description.Length > 0 ? part.Description : part.Id;
        return PartPath(parts, part.ParentPartId) is { } parent ? $"{parent} / {name}" : name;
    }
}
