using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Screenplay.Drafts;

namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>
/// Turns a story draft into ThePlot rows, replacing the imported document on the desk. Names and
/// labels the draft uses but never declared (a speaker, a heading's location, a transition, a
/// character kind) are added to the library or the story's vocabulary rather than dropped.
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

    internal static Story Build(StoryDraft draft)
    {
        var story = new Story { Title = Trim(draft.Title, NameLength, "Untitled") };
        var characters = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
        var locations = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);
        var props = new Dictionary<string, Prop>(StringComparer.OrdinalIgnoreCase);

        var timesOfDay = DefaultVocabulary.TimesOfDay.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var transitions = DefaultVocabulary.Transitions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kinds = DefaultVocabulary.CharacterKinds.ToDictionary(k => k.Label, k => k.Label, StringComparer.OrdinalIgnoreCase);

        foreach (var label in draft.Vocabulary?.TimesOfDay ?? [])
        {
            AddLabel(timesOfDay, story.Vocabulary.TimesOfDay, label);
        }

        foreach (var label in draft.Vocabulary?.Transitions ?? [])
        {
            AddLabel(transitions, story.Vocabulary.Transitions, label);
        }

        foreach (var kind in draft.Vocabulary?.CharacterKinds ?? [])
        {
            AddKind(story, kinds, kind.Label, kind.AvatarShape);
        }

        foreach (var c in draft.Characters ?? [])
        {
            AddCharacter(story, characters, c.Name, AddKind(story, kinds, c.Kind, avatarShape: null));
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
                    if (Element(story, element, characters, locations, props, timesOfDay, transitions) is { } row)
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
        Dictionary<string, Prop> props,
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

                row.TimeOfDay = AddLabel(timesOfDay, story.Vocabulary.TimesOfDay, element.TimeOfDay);
                return row;
            }

            case SceneElementType.Transition:
            {
                var transition = AddLabel(transitions, story.Vocabulary.Transitions, element.Transition)
                                 ?? DefaultVocabulary.Transitions[0];
                return new SceneElement { Type = type, Transition = transition };
            }

            default:
            {
                if (string.IsNullOrWhiteSpace(element.Text))
                {
                    return null;
                }

                var row = new SceneElement { Type = type, Text = element.Text.Trim() };
                row.Mentions = Mentions(row.Text, element.Mentions, characters, locations, props);
                if (type == SceneElementType.Dialogue)
                {
                    if (!string.IsNullOrWhiteSpace(element.Speaker))
                    {
                        row.Speaker = characters.TryGetValue(element.Speaker.Trim(), out var known)
                            ? known
                            : AddCharacter(story, characters, element.Speaker, DefaultVocabulary.CharacterKinds[0].Label);
                    }

                    row.Parenthetical = string.IsNullOrWhiteSpace(element.Parenthetical)
                        ? null
                        : Trim(element.Parenthetical, NameLength, "");
                }

                return row;
            }
        }
    }

    /// <summary>
    /// Keeps the mentions the API's scene validator would accept: each starts at an @ inside the text,
    /// none overlap, and each names a library entry of its kind.
    /// </summary>
    private static List<SceneElementMention> Mentions(
        string text,
        IReadOnlyList<MentionDraft>? drafts,
        Dictionary<string, Character> characters,
        Dictionary<string, Location> locations,
        Dictionary<string, Prop> props)
    {
        var mentions = new List<SceneElementMention>();
        var end = 0;
        foreach (var draft in (drafts ?? []).OrderBy(m => m.Offset))
        {
            if (draft.Offset < end || draft.Length < 2 || draft.Offset + draft.Length > text.Length
                || text[draft.Offset] != '@' || string.IsNullOrWhiteSpace(draft.Name))
            {
                continue;
            }

            var name = draft.Name.Trim();
            var mention = new SceneElementMention { Offset = draft.Offset, Length = draft.Length };
            switch (draft.Kind)
            {
                case MentionKind.Character when characters.TryGetValue(name, out var character):
                    mention.Character = character;
                    break;
                case MentionKind.Location when locations.TryGetValue(name, out var location):
                    mention.Location = location;
                    break;
                case MentionKind.Prop when props.TryGetValue(name, out var prop):
                    mention.Prop = prop;
                    break;
                default:
                    continue;
            }

            mentions.Add(mention);
            end = draft.Offset + draft.Length;
        }

        return mentions;
    }

    private static Character AddCharacter(Story story, Dictionary<string, Character> characters, string? name, string kind)
    {
        var trimmed = Trim(name, NameLength, "Unknown");
        if (characters.TryGetValue(trimmed, out var existing))
        {
            return existing;
        }

        var character = new Character
        {
            Name = trimmed,
            Kind = kind,
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

    /// <summary>Times of day and transitions are stored uppercased, as headings print them.</summary>
    /// <returns>The label as the vocabulary spells it, or null when blank.</returns>
    private static string? AddLabel(HashSet<string> known, List<string> added, string? value)
    {
        if (Label(value) is not { } label)
        {
            return null;
        }

        label = label.ToUpperInvariant();
        if (known.Add(label))
        {
            added.Add(label);
        }

        return label;
    }

    /// <returns>The kind as the vocabulary spells it; a blank kind is the first default.</returns>
    private static string AddKind(Story story, Dictionary<string, string> kinds, string? value, string? avatarShape)
    {
        if (Label(value) is not { } label)
        {
            return DefaultVocabulary.CharacterKinds[0].Label;
        }

        if (kinds.TryGetValue(label, out var known))
        {
            return known;
        }

        kinds[label] = label;
        story.Vocabulary.CharacterKinds.Add(new CharacterKind
        {
            Label = label,
            AvatarShape = Enum.TryParse<AvatarShape>(avatarShape, ignoreCase: true, out var shape) && Enum.IsDefined(shape)
                ? shape
                : AvatarShape.Circle,
        });
        return label;
    }

    /// <summary>Screenplays write transitions as "CUT TO:"; the vocabulary drops the colon.</summary>
    private static string? Label(string? value)
    {
        var label = value?.Trim().TrimEnd(':').TrimEnd() ?? "";
        if (label.Length == 0)
        {
            return null;
        }

        return label.Length > LabelLength ? label[..LabelLength].TrimEnd() : label;
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
