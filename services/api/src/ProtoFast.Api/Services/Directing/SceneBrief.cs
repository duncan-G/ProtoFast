using System.Text;
using ProtoFast.Api.Services.Screenplays;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;

using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using LocationSettingRecord = ProtoFast.Data.ThePlot.Entities.LocationSetting;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// The scene as a model reads it: elements labelled <c>e1…eN</c>, and the cast, props and
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

        var characterIds = Elements
            .SelectMany(e => e.Mentions.Select(m => m.CharacterId).Append(e.Type == SceneElementTypeRecord.Dialogue ? e.SpeakerId : null))
            .OfType<Guid>()
            .ToHashSet();
        Cast = context.Story.Characters.Where(c => characterIds.Contains(c.Id)).ToList();
        var propIds = Elements.SelectMany(e => e.Mentions.Select(m => m.PropId)).OfType<Guid>().ToHashSet();
        Props = context.Story.Props.Where(p => propIds.Contains(p.Id)).ToList();
        var locationIds = Elements.Select(e => e.LocationId).OfType<Guid>().ToHashSet();
        Locations = context.Story.Locations.Where(l => locationIds.Contains(l.Id)).ToList();
    }

    public DirectedScene Context { get; }

    public List<SceneElementRecord> Elements { get; }

    public IReadOnlyList<CharacterRecord> Cast { get; }

    public IReadOnlyList<PropRecord> Props { get; }

    /// <summary>Where the scene's headings put it.</summary>
    public IReadOnlyList<LocationRecord> Locations { get; }

    public string Label(Guid elementId) => _labels[elementId];

    public SceneElementRecord? Element(string? label) =>
        label is not null && _byLabel.TryGetValue(label.Trim(), out var element) ? element : null;

    /// <summary>The 0-based index of a labelled element, or null.</summary>
    public int? Index(string? label) => Element(label) is { } element ? Elements.IndexOf(element) : null;

    public CharacterRecord? CharacterNamed(string? name) =>
        name is null ? null : Context.Story.Characters.FirstOrDefault(c => LibraryNames.Same(c.Name, name));

    public PropRecord? PropNamed(string? name) =>
        name is null ? null : Context.Story.Props.FirstOrDefault(p => LibraryNames.Same(p.Name, name));

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

    /// <summary>Mentions are written <c>@Name</c>; the model reads the name alone.</summary>
    public static string Plain(string? text) => (text ?? "").Replace("@", "");
}
