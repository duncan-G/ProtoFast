using ProtoFast.Api.Services.Directing;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// Gathers the pictures that already exist and could show a frame what it must look like: each
/// character's picture in the look they are in (the library's when the look is the sheet's), the
/// library's pictures of the place, extras and props, the nearest pictured frames around it in
/// its scene, and the latest pictured frames of earlier scenes in the same place or with the same
/// people. Nothing is generated to serve as a reference; a frame in the middle of an unpictured
/// scene is composed from whatever exists.
/// </summary>
public sealed class ReferenceCollector(ImageStaleness staleness)
{
    private const int NeighboursBefore = 2;
    private const int NeighboursAfter = 1;
    private const int EarlierFrames = 3;

    /// <param name="scene">This frame's scene, every frame resolved.</param>
    /// <param name="earlier">Earlier scenes, nearest first, every frame resolved.</param>
    public IReadOnlyList<ReferenceCandidate> Collect(
        StoryRecord story,
        FrameContents frame, IReadOnlyList<FrameContents> scene, IReadOnlyList<IReadOnlyList<FrameContents>> earlier)
    {
        List<ReferenceCandidate> candidates = [];
        void Add(ReferenceSource source, Guid subjectId, MediaAssetRecord asset, string name, string caption, bool stale)
        {
            if (candidates.All(c => c.Asset.Id != asset.Id))
            {
                candidates.Add(new ReferenceCandidate($"r{candidates.Count + 1}", source, subjectId, asset, name, caption, stale));
            }
        }

        foreach (var shown in frame.OnScreen)
        {
            var name = shown.Character.Name;
            var sheet = shown.Look.Look.IsSheet(shown.Character);
            if (!sheet && shown.Look.Image is { } look)
            {
                var carrying = shown.Holding.Count == 0 ? "" : $", carrying {string.Join(" and ", shown.Holding.Select(p => p.Name))}";
                Add(ReferenceSource.Look, shown.Character.Id, look, $"{name} ({LookName(shown)})",
                    $"The reference sheet of {name} in the look they have in this frame{carrying}. Draw {name}"
                    + $"{(shown.Holding.Count == 0 ? "" : " and what they carry")} as this sheet shows, in the pose the description asks for.",
                    staleness.Look(shown.Character, shown.Look.Look, story, look));
            }
            else if (shown.Character.Image is { } image)
            {
                var wearing = shown.Character.Wardrobes.OrderBy(w => w.Position).FirstOrDefault()?.Label;
                Add(ReferenceSource.Character, shown.Character.Id, image, name,
                    $"The library's reference sheet of {name}{(wearing is null ? "" : $", wearing {wearing}")}"
                    + $"{(sheet ? "" : $"; in this frame {name} looks different, as the description says")}.",
                    staleness.Character(shown.Character, image));
            }
        }

        if (frame.Location is { Image: { } plate } location)
        {
            Add(ReferenceSource.Location, location.Id, plate, location.Name,
                $"The library's establishing shot of {location.Name}, empty.",
                staleness.Location(location, plate));
        }

        foreach (var extra in frame.Extras)
        {
            if (extra.Image is { } image)
            {
                Add(ReferenceSource.Extra, extra.Id, image, extra.Name,
                    $"The library's reference picture of {extra.Name}, extras in the background. Draw them as these people, as many as the description says.",
                    staleness.Extra(extra, image));
            }
        }

        foreach (var prop in frame.InView)
        {
            if (prop.Prop.Image is { } image)
            {
                Add(ReferenceSource.Prop, prop.Prop.Id, image, prop.Prop.Name,
                    $"The library's photograph of {prop.Prop.Name}, on its own.",
                    staleness.Prop(prop.Prop, image));
            }
        }

        var before = scene.Where(f => f.Number < frame.Number && f.Frame.Image is not null)
            .OrderByDescending(f => f.Number).Take(NeighboursBefore);
        var after = scene.Where(f => f.Number > frame.Number && f.Frame.Image is not null)
            .OrderBy(f => f.Number).Take(NeighboursAfter);
        foreach (var neighbour in before.Concat(after))
        {
            var distance = neighbour.Number - frame.Number;
            var where = distance < 0 ? $"{-distance} before this one" : $"{distance} after this one";
            Add(ReferenceSource.Frame, neighbour.Frame.Id, neighbour.Frame.Image!, $"Frame {neighbour.Number}",
                $"Frame {neighbour.Number} of this scene ({where}), showing {neighbour.Summary()}.",
                staleness.Frame(neighbour, neighbour.Frame.Image!, story));
        }

        // The latest earlier picture of the same place, then of each person not pictured nearby yet.
        var taken = 0;
        var people = frame.OnScreen.Select(c => c.Character.Id).ToHashSet();
        var placeFound = frame.Location is null;
        foreach (var frames in earlier.TakeWhile(_ => taken < EarlierFrames))
        {
            foreach (var previous in frames.Where(f => f.Frame.Image is not null).OrderByDescending(f => f.Number))
            {
                if (taken >= EarlierFrames)
                {
                    break;
                }

                var samePlace = !placeFound && previous.Location?.Id == frame.Location!.Id;
                var shared = previous.OnScreen.Select(c => c.Character.Id).Where(people.Contains).ToList();
                if (!samePlace && shared.Count == 0)
                {
                    continue;
                }

                placeFound |= samePlace;
                people.ExceptWith(shared);
                taken++;
                var title = SceneTitle(previous.Scene);
                Add(ReferenceSource.Frame, previous.Frame.Id, previous.Frame.Image!, $"Frame {previous.Number} of {title}",
                    $"Frame {previous.Number} of an earlier scene, \"{title}\"{(previous.Scene.StoryDay is { } day ? $" (story day {day})" : "")}, showing {previous.Summary()}.",
                    staleness.Frame(previous, previous.Frame.Image!, story));
            }
        }

        return candidates;
    }

    /// <summary>"carrying Refrigerated cooler", "hood up", "Night shift"</summary>
    private static string LookName(FrameCharacter shown)
    {
        if (shown.Holding.Count > 0)
        {
            return $"carrying {string.Join(" and ", shown.Holding.Select(p => p.Name))}";
        }

        var look = shown.Look.Look;
        var wardrobe = shown.Character.Wardrobes.FirstOrDefault(w => w.Id == look.WardrobeId)?.Label;
        return new[] { look.AppearanceChange, look.WardrobeChange, wardrobe ?? "" }.FirstOrDefault(p => p.Trim().Length > 0)?.Trim() ?? "look";
    }

    private static string SceneTitle(SceneRecord scene) => scene.Title.Trim().Length > 0 ? scene.Title.Trim() : $"scene {scene.Position + 1}";

    /// <summary>Every frame of the scene and of the scenes before it, resolved; earlier scenes nearest first.</summary>
    public static (IReadOnlyList<FrameContents> Scene, IReadOnlyList<IReadOnlyList<FrameContents>> Earlier) Resolve(DirectedScene directed) =>
        (FrameContents.ForScene(directed),
            directed.Earlier.Reverse()
                .Where(s => s.Frames.Any(f => f.Image is not null))
                .Select(s => FrameContents.ForScene(new DirectedScene(s, directed.Story, [], DirectionScope.References(s, directed.Story))))
                .ToList());
}
