using ProtoFast.Api.Services.Directing;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using LocationVariationRecord = ProtoFast.Data.ThePlot.Entities.LocationVariation;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
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
    IReadOnlyList<FrameExtraView> Extras,
    string? Shot,
    string? Angle)
{
    /// <summary>The previous frame, when this one is a variation of it and it has a picture to edit.</summary>
    public FrameContents? VariationOf { get; init; }

    /// <summary>The part of <see cref="Location"/> the camera is in; null where its establishing shot stands.</summary>
    public LocationVariationRecord? LocationVariation { get; init; }

    /// <summary>
    /// A variation is pictured from the previous picture with the camera moved, rather than as an edit
    /// of it, when the author says so or when it is at another part of the place.
    /// </summary>
    public bool MovesCamera => Frame.VariationMovesCamera || (VariationOf is { } previous && !SamePart(previous));

    /// <summary>The same place, and the same part of it.</summary>
    public bool SamePart(FrameContents other) =>
        Location?.Id == other.Location?.Id && LocationVariation?.Id == other.LocationVariation?.Id;

    public static IReadOnlyList<FrameContents> ForScene(DirectedScene directed)
    {
        var brief = new SceneBrief(directed);
        List<FrameContents> frames = [];
        foreach (var frame in directed.Scene.Frames.OrderBy(f => f.Position).ThenBy(f => f.Id))
        {
            var previous = frames.LastOrDefault();
            frames.Add(Resolve(brief, frame, frames.Count + 1) with
            {
                VariationOf = frame.Variation.Trim().Length > 0 && previous?.Frame.Image is not null ? previous : null,
            });
        }

        return frames;
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
                placement?.Depth ?? "",
                placement?.Mark ?? "",
                placement?.Facing ?? "",
                placement?.Moving ?? "",
                placement?.Looking ?? "",
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
            var own = frame.Props.FirstOrDefault(p => p.PropId == prop.Id);
            if (own?.InView ?? inherited)
            {
                inView.Add(new FramePropView(prop, state?.State ?? [], holder?.Character, own?.Screen ?? "", own?.Depth ?? "", own?.Mark ?? ""));
            }
        }

        var extras = scene.Extras
            .Select(e => (Scene: e, Own: frame.Extras.FirstOrDefault(f => f.ExtraId == e.ExtraId)))
            .Where(e => e.Own?.OnScreen ?? e.Scene.OnScreen)
            .Select(e => story.Extras.FirstOrDefault(x => x.Id == e.Scene.ExtraId) is { } record
                ? new FrameExtraView(
                    record, e.Own?.Screen ?? "", e.Own?.Depth ?? "", e.Own?.Mark ?? "", e.Own?.Facing ?? "", e.Own?.Moving ?? "",
                    e.Own?.Looking ?? "", e.Own?.Pose ?? "")
                : null)
            .OfType<FrameExtraView>()
            .OrderBy(e => e.Extra.Name)
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
            frame.Angle ?? scene.Angle)
        {
            LocationVariation = location?.Variations.FirstOrDefault(v => v.Id == frame.LocationVariationId),
        };
    }

    /// <summary>
    /// Whose cast and props this frame's picture shows: a same-shot variation is an edit of the picture
    /// before it, so it shows what that one does, whatever its own rows say.
    /// </summary>
    public FrameContents Pictured => VariationOf is { } previous && !MovesCamera ? previous.Pictured : this;

    /// <summary>The people, extras and props in this frame that <paramref name="previous"/> doesn't show.</summary>
    public IReadOnlySet<Guid> NewSince(FrameContents previous) => Shown().Except(previous.Shown()).ToHashSet();

    /// <summary>Who and what <paramref name="previous"/> shows that this frame doesn't, by name.</summary>
    public IReadOnlyList<string> GoneSince(FrameContents previous)
    {
        var shown = Shown().ToHashSet();
        return previous.OnScreen.Where(c => !shown.Contains(c.Character.Id)).Select(c => c.Character.Name)
            .Concat(previous.Extras.Where(e => !shown.Contains(e.Extra.Id)).Select(e => e.Extra.Name))
            .Concat(previous.InView.Where(p => !shown.Contains(p.Prop.Id)).Select(p => p.Prop.Name))
            .ToList();
    }

    private IEnumerable<Guid> Shown() =>
        OnScreen.Select(c => c.Character.Id).Concat(Extras.Select(e => e.Extra.Id)).Concat(InView.Select(p => p.Prop.Id));

    /// <summary>"MARA, TOM at the Harbour (night)"</summary>
    public string Summary()
    {
        var people = OnScreen.Select(c => c.Character.Name).Concat(Extras.Select(e => e.Extra.Name)).ToList();
        var who = people.Count == 0 ? "nobody" : string.Join(", ", people);
        var where = Location is null ? "" : $" at {Location.Name}{(LocationVariation is null ? "" : $" ({LocationVariation.Name})")}";
        var when = string.IsNullOrWhiteSpace(TimeOfDay) ? "" : $" ({TimeOfDay.Trim().ToLowerInvariant()})";
        var shot = Shot is null ? "" : $", {Shot} shot";
        var beat = string.IsNullOrWhiteSpace(Frame.Beat) ? "" : $": {Frame.Beat.Trim().TrimEnd('.')}";
        return $"{who}{where}{when}{shot}{beat}";
    }

    private static string Join(params string?[] parts) =>
        string.Join(' ', parts.Select(p => p?.Trim() ?? "").Where(p => p.Length > 0));
}
