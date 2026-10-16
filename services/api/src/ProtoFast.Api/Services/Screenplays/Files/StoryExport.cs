using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;

namespace ProtoFast.Api.Services.Screenplays.Files;

/// <summary>
/// Writes a story as a <see cref="TheplotFile"/>. The library is sorted by name and numbered in
/// that order (c1, l1, p1), so the same story always exports to the same bytes.
/// </summary>
public static class StoryExport
{
    /// <param name="story">With its outline and library.</param>
    /// <param name="elements">Every scene's rows in order, with their mentions.</param>
    public static TheplotFile ToFile(StoryRecord story, ILookup<Guid, SceneElementRecord> elements)
    {
        var characters = story.Characters.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        var locations = story.Locations.OrderBy(l => l.Name, StringComparer.Ordinal).ToList();
        var props = story.Props.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var keys = new FileKeys(
            Number(characters.Select(c => c.Id), "c"),
            Number(locations.Select(l => l.Id), "l"),
            Number(props.Select(p => p.Id), "p"));

        return new TheplotFile
        {
            Format = TheplotFile.FormatName,
            Version = TheplotFile.CurrentVersion,
            Title = story.Title,
            Vocabulary = new FileVocabulary
            {
                TimesOfDay = [.. story.Vocabulary.TimesOfDay],
                Transitions = [.. story.Vocabulary.Transitions],
                Extensions = [.. story.Vocabulary.Extensions],
                CharacterKinds = story.Vocabulary.CharacterKinds
                    .Select(k => new FileCharacterKind { Label = k.Label, AvatarShape = k.AvatarShape })
                    .ToList(),
            },
            Characters = characters
                .Select(c => new FileCharacter { Id = keys.Characters[c.Id], Name = c.Name, Kind = c.Kind, Hue = c.Hue })
                .ToList(),
            Locations = locations
                .Select(l => new FileLocation { Id = keys.Locations[l.Id], Name = l.Name, Setting = l.Setting, Hue = l.Hue })
                .ToList(),
            Props = props.Select(p => new FileProp { Id = keys.Props[p.Id], Name = p.Name }).ToList(),
            Containers = story.Containers
                .OrderBy(c => c.Position).ThenBy(c => c.Id)
                .Select(c => new FileContainer
                {
                    Label = c.Label,
                    Scenes = c.Scenes
                        .OrderBy(s => s.Position).ThenBy(s => s.Id)
                        .Select(s => new FileScene
                        {
                            Title = s.Title,
                            Elements = elements[s.Id].Select(e => ToFile(e, keys)).ToList(),
                        })
                        .ToList(),
                })
                .ToList(),
        };
    }

    // A reference the library no longer has was deleted while the export read; the delete unlinks it.
    private static FileElement ToFile(SceneElementRecord element, FileKeys keys)
    {
        var mentions = element.Mentions
            .OrderBy(m => m.Offset)
            .Select(m => ToFile(m, keys))
            .OfType<FileMention>()
            .ToList();
        return new FileElement
        {
            Type = element.Type,
            Text = element.Text,
            Location = Key(keys.Locations, element.LocationId),
            TimeOfDay = element.TimeOfDay,
            Speaker = Key(keys.Characters, element.SpeakerId),
            Extension = element.Extension,
            Parenthetical = element.Parenthetical,
            Transition = element.Transition,
            Mentions = mentions.Count == 0 ? null : mentions,
        };
    }

    private static FileMention? ToFile(SceneElementMentionRecord mention, FileKeys keys)
    {
        var character = Key(keys.Characters, mention.CharacterId);
        var location = Key(keys.Locations, mention.LocationId);
        var prop = Key(keys.Props, mention.PropId);
        return (character ?? location ?? prop) is null
            ? null
            : new FileMention
            {
                Offset = mention.Offset,
                Length = mention.Length,
                Character = character,
                Location = location,
                Prop = prop,
                Tag = mention.IsTag,
            };
    }

    private static string? Key(IReadOnlyDictionary<Guid, string> keys, Guid? id) =>
        id is { } value && keys.TryGetValue(value, out var key) ? key : null;

    private static Dictionary<Guid, string> Number(IEnumerable<Guid> ids, string prefix) =>
        ids.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => $"{prefix}{p.index + 1}");
}
