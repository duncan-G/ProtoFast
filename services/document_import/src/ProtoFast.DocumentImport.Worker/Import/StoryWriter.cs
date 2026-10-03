using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Screenplay.Verifiers;

namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>
/// Turns a story draft into ThePlot rows, replacing the imported document on the desk. Names the
/// draft uses but never declared (a speaker, a heading's location) are added to the library rather
/// than dropped.
/// </summary>
public sealed class StoryWriter(
    UserContext userContext,
    IUnitOfWorkFactory unitOfWorkFactory,
    IDocumentRepository documents,
    IStoryRepository stories)
{
    private const int NameLength = 255;
    private const int LabelLength = 64;

    /// <summary>False once the document has become a story (or was deleted), so a redelivery is a no-op.</summary>
    public async Task<bool> IsPendingAsync(string userId, string documentId, CancellationToken ct)
    {
        using var user = userContext.SetCurrentUser(userId);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(IsPendingAsync));
        return await documents.ExistsByKeyAsync(documentId, ct);
    }

    /// <summary>
    /// Saves the story and removes the document row in one commit. The stored file is kept. Returns
    /// null, writing nothing, when the document is already gone.
    /// </summary>
    public async Task<Guid?> WriteAsync(string userId, string documentId, StoryDraft draft, CancellationToken ct)
    {
        using var user = userContext.SetCurrentUser(userId);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(WriteAsync));
        if (await documents.GetByKeyAsync(documentId, ct) is not { } document)
        {
            return null;
        }

        var story = Build(draft);
        await stories.AddAsync(story, ct);
        await documents.RemoveAsync(document, ct);
        await unitOfWork.CommitAsync(ct);
        return story.Id;
    }

    private static Story Build(StoryDraft draft)
    {
        var story = new Story { Title = Trim(draft.Title, NameLength, "Untitled") };
        var characters = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
        var locations = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);
        var props = new Dictionary<string, Prop>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in draft.Characters ?? [])
        {
            AddCharacter(story, characters, c.Name, c.Kind);
        }

        foreach (var l in draft.Locations ?? [])
        {
            AddLocation(story, locations, l.Name, l.Setting);
        }

        foreach (var p in draft.Props ?? [])
        {
            var name = Trim(p.Name, NameLength, "");
            if (name.Length > 0 && !props.ContainsKey(name))
            {
                var prop = new Prop { Name = name };
                props[name] = prop;
                story.Props.Add(prop);
            }
        }

        var timesOfDay = DefaultVocabulary.TimesOfDay.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var transitions = DefaultVocabulary.Transitions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (container, containerIndex) in (draft.Containers ?? []).Select((c, i) => (c, i)))
        {
            var containerRow = new Container
            {
                Label = Trim(container.Label, NameLength, $"Act {containerIndex + 1}"),
                Position = containerIndex,
            };
            story.Containers.Add(containerRow);

            foreach (var (scene, sceneIndex) in (container.Scenes ?? []).Select((s, i) => (s, i)))
            {
                var sceneRow = new Scene { Title = Trim(scene.Title, NameLength, $"Scene {sceneIndex + 1}"), Position = sceneIndex };
                containerRow.Scenes.Add(sceneRow);

                foreach (var element in scene.Elements ?? [])
                {
                    if (Element(story, element, characters, locations, timesOfDay, transitions) is { } row)
                    {
                        row.Position = sceneRow.Elements.Count;
                        sceneRow.Elements.Add(row);
                    }
                }
            }
        }

        return story;
    }

    private static SceneElement? Element(
        Story story,
        SceneElementDraft element,
        Dictionary<string, Character> characters,
        Dictionary<string, Location> locations,
        HashSet<string> timesOfDay,
        HashSet<string> transitions)
    {
        if (!Enum.TryParse<SceneElementType>(element.Type, ignoreCase: true, out var type))
        {
            return null;
        }

        switch (type)
        {
            case SceneElementType.Heading:
            {
                var row = new SceneElement { Type = type };
                if (!string.IsNullOrWhiteSpace(element.Location))
                {
                    row.Location = locations.TryGetValue(element.Location.Trim(), out var known)
                        ? known
                        : AddLocation(story, locations, element.Location, setting: null);
                }

                if (!string.IsNullOrWhiteSpace(element.TimeOfDay))
                {
                    var time = Trim(element.TimeOfDay.ToUpperInvariant(), LabelLength, "");
                    if (timesOfDay.Add(time))
                    {
                        story.Vocabulary.TimesOfDay.Add(time);
                    }

                    row.TimeOfDay = time;
                }

                return row;
            }

            case SceneElementType.Transition:
            {
                var transition = Trim((element.Transition ?? "CUT TO").ToUpperInvariant(), LabelLength, "CUT TO");
                if (transitions.Add(transition))
                {
                    story.Vocabulary.Transitions.Add(transition);
                }

                return new SceneElement { Type = type, Transition = transition };
            }

            default:
            {
                if (string.IsNullOrWhiteSpace(element.Text))
                {
                    return null;
                }

                var row = new SceneElement { Type = type, Text = element.Text.Trim() };
                if (type == SceneElementType.Dialogue)
                {
                    if (!string.IsNullOrWhiteSpace(element.Speaker))
                    {
                        row.Speaker = characters.TryGetValue(element.Speaker.Trim(), out var known)
                            ? known
                            : AddCharacter(story, characters, element.Speaker, kind: null);
                    }

                    row.Parenthetical = string.IsNullOrWhiteSpace(element.Parenthetical)
                        ? null
                        : Trim(element.Parenthetical, NameLength, "");
                }

                return row;
            }
        }
    }

    private static Character AddCharacter(Story story, Dictionary<string, Character> characters, string? name, string? kind)
    {
        var trimmed = Trim(name, NameLength, "Unknown");
        if (characters.TryGetValue(trimmed, out var existing))
        {
            return existing;
        }

        var character = new Character
        {
            Name = trimmed,
            Kind = kind is not null && StoryDraftVerifier.KnownKinds.Contains(kind)
                ? DefaultVocabulary.CharacterKinds.First(k => k.Label.Equals(kind, StringComparison.OrdinalIgnoreCase)).Label
                : DefaultVocabulary.CharacterKinds[0].Label,
            Hue = Hue(trimmed),
        };
        characters[trimmed] = character;
        story.Characters.Add(character);
        return character;
    }

    private static Location AddLocation(Story story, Dictionary<string, Location> locations, string? name, string? setting)
    {
        var trimmed = Trim(name, NameLength, "Unknown");
        if (locations.TryGetValue(trimmed, out var existing))
        {
            return existing;
        }

        var location = new Location
        {
            Name = trimmed,
            Setting = Enum.TryParse<LocationSetting>(setting, ignoreCase: true, out var parsed) ? parsed : LocationSetting.Interior,
            Hue = Hue(trimmed),
        };
        locations[trimmed] = location;
        story.Locations.Add(location);
        return location;
    }

    /// <summary>A stable hue per name, so re-imports colour the same character the same way.</summary>
    private static int Hue(string name)
    {
        var hash = 17;
        foreach (var c in name.ToUpperInvariant())
        {
            hash = unchecked(hash * 31 + c);
        }

        return Math.Abs(hash % 360);
    }

    private static string Trim(string? value, int max, string fallback)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return fallback;
        }

        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
