using System.Text;
using ProtoFast.Api.Services.Screenplays;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;

using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using SceneLookChangeRecord = ProtoFast.Data.ThePlot.Entities.SceneLookChange;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using LocationSettingRecord = ProtoFast.Data.ThePlot.Entities.LocationSetting;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// The scene as a model reads it: elements labelled <c>e1…eN</c>, and the cast, extras, props and
/// locations by name. Labels and names map back to ids, so the model never sees a GUID.
/// </summary>
public sealed class SceneBrief
{
    private readonly Dictionary<string, SceneElementRecord> _byLabel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _labels = [];

    public SceneBrief(DirectedScene context)
    {
        Context = context;
        Elements = context.Scene.Elements.OrderBy(e => e.Position).ThenBy(e => e.Id).ToList();
        for (var i = 0; i < Elements.Count; i++)
        {
            var label = $"e{i + 1}";
            _byLabel[label] = Elements[i];
            _labels[Elements[i].Id] = label;
        }

        // A row in the director layer keeps someone in the scene the text no longer names.
        var characterIds = Elements
            .SelectMany(e => e.Mentions.Select(m => m.CharacterId).Append(e.Type == SceneElementTypeRecord.Dialogue ? e.SpeakerId : null))
            .OfType<Guid>()
            .Concat(context.Scene.Appearances.Select(a => a.CharacterId))
            .Concat(context.Scene.LookChanges.Select(c => c.CharacterId))
            .ToHashSet();
        var propIds = Elements.SelectMany(e => e.Mentions.Select(m => m.PropId)).OfType<Guid>()
            .Concat(context.Scene.PropStates.Select(p => p.PropId))
            .Concat(context.Scene.Appearances.SelectMany(a => a.Holding))
            .Concat(context.Scene.LookChanges.SelectMany(c => c.Holding))
            .ToHashSet();
        var locationIds = Elements.Select(e => e.LocationId).OfType<Guid>()
            .Concat(context.Scene.LocationStates.Select(l => l.LocationId))
            .ToHashSet();
        Include(characterIds, propIds, locationIds);
        _library = [.. context.Story.Extras];
        var extraIds = context.Scene.Extras.Select(e => e.ExtraId).ToHashSet();
        Extras = _library.Where(e => extraIds.Contains(e.Id)).ToList();
    }

    private readonly List<ExtraRecord> _library;

    public DirectedScene Context { get; }

    public List<SceneElementRecord> Elements { get; }

    public IReadOnlyList<CharacterRecord> Cast { get; private set; } = [];

    public IReadOnlyList<PropRecord> Props { get; private set; } = [];

    public IReadOnlyList<ExtraRecord> Extras { get; private set; } = [];

    /// <summary>The story's extras and any a directing pass has added since.</summary>
    public IReadOnlyList<ExtraRecord> ExtraLibrary => _library;

    /// <summary>Where the scene's headings put it, and where the director layer places the rest.</summary>
    public IReadOnlyList<LocationRecord> Locations { get; private set; } = [];

    /// <summary>Some row has no heading that names a location.</summary>
    public bool Unplaced => Elements.Count > 0 && Elements.Select((_, i) => HeadingAt(i)?.LocationId).Any(id => id is null);

    /// <summary>Adds library entries to the scene, keeping library order.</summary>
    public void Include(IEnumerable<Guid> characterIds, IEnumerable<Guid> propIds, IEnumerable<Guid> locationIds)
    {
        var cast = Cast.Select(c => c.Id).Concat(characterIds).ToHashSet();
        Cast = Context.Story.Characters.Where(c => cast.Contains(c.Id)).ToList();
        var props = Props.Select(p => p.Id).Concat(propIds).ToHashSet();
        Props = Context.Story.Props.Where(p => props.Contains(p.Id)).ToList();
        var locations = Locations.Select(l => l.Id).Concat(locationIds).ToHashSet();
        Locations = Context.Story.Locations.Where(l => locations.Contains(l.Id)).ToList();
    }

    /// <summary>Sets the scene's extras, adding new ones to the library the brief reads.</summary>
    public void SetExtras(IEnumerable<ExtraRecord> extras)
    {
        var list = extras.Distinct().ToList();
        _library.AddRange(list.Where(e => !_library.Contains(e)));
        Extras = _library.Where(list.Contains).ToList();
    }

    public ExtraRecord? ExtraNamed(string? name) =>
        name is null ? null : _library.FirstOrDefault(e => LibraryNames.Same(e.Name, name));

    public string Label(Guid elementId) => _labels[elementId];

    public SceneElementRecord? Element(string? label) =>
        label is not null && _byLabel.TryGetValue(label.Trim(), out var element) ? element : null;

    /// <summary>The 0-based index of a labelled element, or null.</summary>
    public int? Index(string? label) => Element(label) is { } element ? Elements.IndexOf(element) : null;

    public CharacterRecord? CharacterNamed(string? name) =>
        name is null ? null : Context.Story.Characters.FirstOrDefault(c => LibraryNames.Same(c.Name, name));

    public PropRecord? PropNamed(string? name) =>
        name is null ? null : Context.Story.Props.FirstOrDefault(p => LibraryNames.Same(p.Name, name));

    /// <summary>The 0-based index of an element, or <see cref="int.MaxValue"/> when it is no longer in the scene.</summary>
    public int Position(Guid elementId) => _labels.ContainsKey(elementId) ? Elements.FindIndex(e => e.Id == elementId) : int.MaxValue;

    public LocationRecord? LocationNamed(string? name) =>
        name is null ? null : Context.Story.Locations.FirstOrDefault(l => LibraryNames.Same(l.Name, name));

    /// <summary>The heading in force at an element: the last one at or before it.</summary>
    public SceneElementRecord? HeadingAt(int index) =>
        Elements.Take(index + 1).LastOrDefault(e => e.Type == SceneElementTypeRecord.Heading);

    public string Heading()
    {
        var heading = Elements.FirstOrDefault(e => e.Type == SceneElementTypeRecord.Heading);
        return heading is null ? "no heading" : HeadingText(heading);
    }

    public string HeadingText(SceneElementRecord heading)
    {
        var location = heading.LocationId is { } id ? Context.Story.Locations.FirstOrDefault(l => l.Id == id) : null;
        var slug = location is null
            ? "NO LOCATION"
            : $"{(location.Setting == LocationSettingRecord.Interior ? "INT." : "EXT.")} {location.Name.ToUpperInvariant()}";
        return heading.TimeOfDay is null ? slug : $"{slug} — {heading.TimeOfDay}";
    }

    /// <summary>Every element on its own line: <c>e3 MARA (whispering): Don't move.</c></summary>
    public string Script()
    {
        var text = new StringBuilder();
        foreach (var element in Elements)
        {
            text.Append(Label(element.Id)).Append(' ').AppendLine(Line(element));
        }

        return text.ToString();
    }

    public string Line(SceneElementRecord element)
    {
        switch (element.Type)
        {
            case SceneElementTypeRecord.Heading:
                return $"[heading] {HeadingText(element)}";
            case SceneElementTypeRecord.Transition:
                return $"[transition] {element.Transition ?? ""}";
            case SceneElementTypeRecord.Dialogue:
                var speaker = element.SpeakerId is { } id
                    ? Context.Story.Characters.FirstOrDefault(c => c.Id == id)?.Name.ToUpperInvariant() ?? "UNKNOWN"
                    : "UNKNOWN";
                var extension = element.Extension is null ? "" : $" ({element.Extension})";
                var parenthetical = element.Parenthetical is null ? "" : $" ({element.Parenthetical})";
                return $"{speaker}{extension}{parenthetical}: {Plain(element.Text)}";
            default:
                return $"[{element.Type.ToString().ToLowerInvariant()}] {Plain(element.Text)}";
        }
    }

    public string CastSheet()
    {
        var text = new StringBuilder();
        foreach (var character in Cast)
        {
            text.Append("- ").Append(character.Name).Append(" (").Append(character.Kind).Append(')');
            if (!string.IsNullOrWhiteSpace(character.Description))
            {
                text.Append(": ").Append(character.Description);
            }

            if (character.Voice is { } voice && (voice.Description.Length > 0 || voice.Baseline.Length > 0))
            {
                text.Append(" Voice: ").Append(voice.Description);
                if (voice.Baseline.Length > 0)
                {
                    text.Append("; baseline delivery: ").Append(voice.Baseline);
                }

                text.Append('.');
            }

            if (character.Wardrobes.Count > 0)
            {
                text.Append(" Wardrobes: ").Append(string.Join(", ", character.Wardrobes.Select(w => $"\"{w.Label}\""))).Append('.');
            }

            text.AppendLine();
        }

        return text.Length == 0 ? "- (nobody is named in this scene)\n" : text.ToString();
    }

    /// <summary>
    /// Each character's look as the scene opens and every line that changes it:
    /// <c>- MARA: as the scene opens, wearing "coat", carrying Lantern; from e7 (drops it): carrying nothing.</c>
    /// </summary>
    public string LookSheet(IReadOnlyList<SceneAppearanceRecord> appearances, IReadOnlyList<SceneLookChangeRecord> changes)
    {
        var text = new StringBuilder();
        foreach (var character in Cast)
        {
            var opening = appearances.FirstOrDefault(a => a.CharacterId == character.Id);
            var steps = Looks.Changes(changes, Position, character.Id).Where(c => _labels.ContainsKey(c.ElementId)).ToList();
            if (opening is null && steps.Count == 0)
            {
                continue;
            }

            text.Append("- ").Append(character.Name).Append(": as the scene opens, ")
                .Append(Describe(character, opening is null ? Look.Plain : Look.Of(opening), opening?.Wardrobe));
            foreach (var step in steps)
            {
                text.Append("; from ").Append(Label(step.ElementId));
                if (!string.IsNullOrWhiteSpace(step.Reason))
                {
                    text.Append(" (").Append(step.Reason.Trim().TrimEnd('.')).Append(')');
                }

                text.Append(": ").Append(Describe(character, Look.Of(step), step.Wardrobe));
            }

            text.AppendLine(".");
        }

        return text.Length == 0 ? "- (no looks yet)\n" : text.ToString();
    }

    private string Describe(CharacterRecord character, Look look, Data.ThePlot.Entities.CharacterWardrobe? added)
    {
        var wardrobe = added ?? character.Wardrobes.FirstOrDefault(w => w.Id == look.WardrobeId)
            ?? character.Wardrobes.OrderBy(w => w.Position).FirstOrDefault();
        var held = look.Holding.Select(id => Context.Story.Props.FirstOrDefault(p => p.Id == id)?.Name).OfType<string>().ToList();
        var parts = new[]
        {
            wardrobe is null ? "" : $"wearing \"{wardrobe.Label}\"",
            look.WardrobeChange,
            look.AppearanceChange,
            held.Count == 0 ? "carrying nothing" : $"carrying {string.Join(" and ", held)}",
        };
        return string.Join(", ", parts.Where(p => p.Length > 0));
    }

    public string PropSheet()
    {
        var text = new StringBuilder();
        foreach (var prop in Props)
        {
            text.Append("- ").Append(prop.Name);
            if (!string.IsNullOrWhiteSpace(prop.Description))
            {
                text.Append(": ").Append(prop.Description);
            }

            text.AppendLine();
        }

        return text.Length == 0 ? "- (none)\n" : text.ToString();
    }

    /// <summary><c>- Pub regulars (12 people): a dozen drinkers in their sixties. Wearing: flat caps, cardigans.</c></summary>
    public static string ExtraSheet(IEnumerable<ExtraRecord> extras)
    {
        var text = new StringBuilder();
        foreach (var extra in extras)
        {
            text.Append("- ").Append(extra.Name);
            if (extra.Headcount is { } count)
            {
                text.Append(" (").Append(Headcount.Words(count)).Append(')');
            }

            if (!string.IsNullOrWhiteSpace(extra.Description))
            {
                text.Append(": ").Append(extra.Description.Trim());
            }

            if (!string.IsNullOrWhiteSpace(extra.Wardrobe))
            {
                text.Append(" Wearing: ").Append(extra.Wardrobe.Trim());
            }

            text.AppendLine();
        }

        return text.Length == 0 ? "- (none)\n" : text.ToString();
    }

    /// <param name="withLayout">Add each place's layout, so staging can say where things are in it.</param>
    public string LocationSheet(bool withLayout = false)
    {
        var text = new StringBuilder();
        foreach (var location in Locations)
        {
            text.Append("- ").Append(location.Name).Append(location.Setting == LocationSettingRecord.Interior ? " (interior)" : " (exterior)");
            if (!string.IsNullOrWhiteSpace(location.Description))
            {
                text.Append(": ").Append(location.Description);
            }

            text.AppendLine();
            if (withLayout && location.Appearance?.Layout.Trim() is { Length: > 0 } layout)
            {
                text.Append("  laid out as (a plan, not a camera): ").AppendLine(layout);
            }

            foreach (var part in location.Variations.OrderBy(v => v.Position))
            {
                text.Append("  - part \"").Append(part.Name).Append('"');
                if (part.Description.Trim().Length > 0)
                {
                    text.Append(": ").Append(part.Description.Trim());
                }

                text.AppendLine();
            }
        }

        return text.Length == 0 ? "- (none)\n" : text.ToString();
    }

    /// <summary>Mentions are written <c>@Name</c>; the model reads the name alone.</summary>
    public static string Plain(string? text) => (text ?? "").Replace("@", "");
}
