using ProtoFast.Api.Services.Directing;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>
/// What one frame shows once it has inherited from its scene: the scene's framing, cast, extras,
/// props and looks unless the frame overrides them, the changes added up (sheet, then the look in force at the
/// frame's last row, then frame), and the place from the frame, else the heading in force, else the
/// scene's only placed location. A prop someone carries is in view exactly when they are, unless the
/// frame says otherwise. Mirrors the client's <c>direction/inheritance.ts</c>.
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
    IReadOnlyList<ExtraRecord> Extras,
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
        var at = brief.Elements.FirstOrDefault(e => e.Id == frame.LastElementId)?.Position ?? int.MaxValue;

        List<FrameCharacter> onScreen = [];
        List<CharacterRecord> offScreen = [];
        HashSet<Guid> held = [];
        foreach (var character in brief.Cast)
        {
            var opening = scene.Appearances.FirstOrDefault(a => a.CharacterId == character.Id);
            var look = Looks.At(scene, character.Id, at);
            held.UnionWith(look.Look.Holding);
            var placement = frame.Placements.FirstOrDefault(p => p.CharacterId == character.Id);
            if (!(placement?.OnScreen ?? opening?.OnScreen ?? true))
            {
                offScreen.Add(character);
                continue;
            }

            var wardrobeId = placement?.WardrobeId ?? look.Look.WardrobeId;
            var wardrobe = look.Change?.Wardrobe is { } changed && placement?.WardrobeId is null
                ? changed
                : character.Wardrobes.FirstOrDefault(w => w.Id == wardrobeId) ?? character.Wardrobes.OrderBy(w => w.Position).FirstOrDefault();
            onScreen.Add(new FrameCharacter(
                character,
                wardrobe,
                Join(look.Look.WardrobeChange, placement?.WardrobeChange),
                Join(look.Look.AppearanceChange, placement?.AppearanceChange),
                placement?.Screen ?? "",
                placement?.Pose ?? "",
                placement?.Expression ?? "",
                look,
                look.Look.Holding.Select(id => story.Props.FirstOrDefault(p => p.Id == id)).OfType<PropRecord>().ToList()));
        }

        List<FramePropView> inView = [];
        foreach (var prop in brief.Props)
        {
            var state = scene.PropStates.FirstOrDefault(s => s.PropId == prop.Id);
            var holder = onScreen.FirstOrDefault(c => c.Holding.Contains(prop));
            var inherited = held.Contains(prop.Id) ? holder is not null : state?.InView ?? true;
            if (frame.Props.FirstOrDefault(p => p.PropId == prop.Id)?.InView ?? inherited)
            {
                inView.Add(new FramePropView(prop, state?.State ?? [], holder?.Character));
            }
        }

        var extras = scene.Extras
            .Where(e => frame.Extras.FirstOrDefault(f => f.ExtraId == e.ExtraId)?.OnScreen ?? e.OnScreen)
            .Select(e => story.Extras.FirstOrDefault(x => x.Id == e.ExtraId))
            .OfType<ExtraRecord>()
            .OrderBy(e => e.Name)
            .ToList();

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
            extras,
            frame.Shot ?? scene.Shot,
            frame.Angle ?? scene.Angle);
    }

    /// <summary>"MARA, TOM at the Harbour (night)"</summary>
    public string Summary()
    {
        var people = OnScreen.Select(c => c.Character.Name).Concat(Extras.Select(e => e.Name)).ToList();
        var who = people.Count == 0 ? "nobody" : string.Join(", ", people);
        var where = Location is null ? "" : $" at {Location.Name}";
        var when = string.IsNullOrWhiteSpace(TimeOfDay) ? "" : $" ({TimeOfDay.Trim().ToLowerInvariant()})";
        var shot = Shot is null ? "" : $", {Shot} shot";
        var beat = string.IsNullOrWhiteSpace(Frame.Beat) ? "" : $": {Frame.Beat.Trim().TrimEnd('.')}";
        return $"{who}{where}{when}{shot}{beat}";
    }

    private static string Join(params string?[] parts) =>
        string.Join(' ', parts.Select(p => p?.Trim() ?? "").Where(p => p.Length > 0));
}
