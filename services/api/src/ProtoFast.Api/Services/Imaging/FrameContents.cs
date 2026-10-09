using ProtoFast.Api.Services.Directing;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// What one frame shows once it has inherited from its scene: the scene's framing, cast, props and
/// looks unless the frame overrides them, the changes added up (sheet, then scene, then frame), and
/// the place from the frame, else the heading in force, else the scene's only placed location.
/// Mirrors the client's <c>direction/inheritance.ts</c>.
/// </summary>
public sealed record FrameContents(
    SceneRecord Scene,
    FrameRecord Frame,
    int Number,
    LocationRecord? Location,
    string? TimeOfDay,
    string LocationChange,
    IReadOnlyList<FrameCharacter> OnScreen,
    IReadOnlyList<CharacterRecord> OffScreen,
    IReadOnlyList<FramePropView> InView,
    string? Shot,
    string? Angle)
{
    public static IReadOnlyList<FrameContents> ForScene(DirectedScene directed)
    {
        var brief = new SceneBrief(directed);
        return directed.Scene.Frames.OrderBy(f => f.Position).ThenBy(f => f.Id)
            .Select((frame, i) => Resolve(brief, frame, i + 1))
            .ToList();
    }

    public static FrameContents Resolve(SceneBrief brief, FrameRecord frame, int number)
    {
        var scene = brief.Context.Scene;
        var story = brief.Context.Story;
        var first = brief.Elements.FindIndex(e => e.Id == frame.FirstElementId);
        var heading = first >= 0 ? brief.HeadingAt(first) : null;
        var locationId = frame.LocationId
                         ?? heading?.LocationId
                         ?? (scene.LocationStates.Count == 1 ? scene.LocationStates[0].LocationId : null);
        var location = locationId is { } id ? story.Locations.FirstOrDefault(l => l.Id == id) : null;
        var sceneChange = location is null ? "" : scene.LocationStates.FirstOrDefault(s => s.LocationId == location.Id)?.Change ?? "";

        List<FrameCharacter> onScreen = [];
        List<CharacterRecord> offScreen = [];
        foreach (var character in brief.Cast)
        {
            var look = scene.Appearances.FirstOrDefault(a => a.CharacterId == character.Id);
            var placement = frame.Placements.FirstOrDefault(p => p.CharacterId == character.Id);
            if (!(placement?.OnScreen ?? look?.OnScreen ?? true))
            {
                offScreen.Add(character);
                continue;
            }

            var wardrobeId = placement?.WardrobeId ?? look?.WardrobeId;
            var wardrobe = character.Wardrobes.FirstOrDefault(w => w.Id == wardrobeId)
                           ?? character.Wardrobes.OrderBy(w => w.Position).FirstOrDefault();
            onScreen.Add(new FrameCharacter(
                character,
                wardrobe,
                Join(look?.WardrobeChange, placement?.WardrobeChange),
                Join(look?.AppearanceChange, placement?.AppearanceChange),
                placement?.Screen ?? "",
                placement?.Pose ?? "",
                placement?.Expression ?? ""));
        }

        List<FramePropView> inView = [];
        foreach (var prop in brief.Props)
        {
            var state = scene.PropStates.FirstOrDefault(s => s.PropId == prop.Id);
            if (frame.Props.FirstOrDefault(p => p.PropId == prop.Id)?.InView ?? state?.InView ?? true)
            {
                inView.Add(new FramePropView(prop, state?.State ?? []));
            }
        }

        return new FrameContents(
            scene,
            frame,
            number,
            location,
            frame.TimeOfDay ?? heading?.TimeOfDay,
            Join(sceneChange, frame.LocationChange),
            onScreen,
            offScreen,
            inView,
            frame.Shot ?? scene.Shot,
            frame.Angle ?? scene.Angle);
    }

    /// <summary>"MARA, TOM at the Harbour (night)"</summary>
    public string Summary()
    {
        var who = OnScreen.Count == 0 ? "nobody" : string.Join(", ", OnScreen.Select(c => c.Character.Name));
        var where = Location is null ? "" : $" at {Location.Name}";
        var when = string.IsNullOrWhiteSpace(TimeOfDay) ? "" : $" ({TimeOfDay.Trim().ToLowerInvariant()})";
        var shot = Shot is null ? "" : $", {Shot} shot";
        var beat = string.IsNullOrWhiteSpace(Frame.Beat) ? "" : $": {Frame.Beat.Trim().TrimEnd('.')}";
        return $"{who}{where}{when}{shot}{beat}";
    }

    private static string Join(params string?[] parts) =>
        string.Join(' ', parts.Select(p => p?.Trim() ?? "").Where(p => p.Length > 0));
}
