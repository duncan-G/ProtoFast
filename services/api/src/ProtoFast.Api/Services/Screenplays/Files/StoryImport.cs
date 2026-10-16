using Google.Protobuf.Collections;
using Grpc.Core;
using ProtoFast.Data.ThePlot.Entities;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using ContainerRecord = ProtoFast.Data.ThePlot.Entities.Container;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;
using StoryVocabularyRecord = ProtoFast.Data.ThePlot.Entities.StoryVocabulary;

namespace ProtoFast.Api.Services.Screenplays.Files;

/// <summary>
/// Turns a <see cref="TheplotFile"/> into a new story under the editor's own rules. Every row gets
/// a fresh id, so one file can be imported any number of times. A row's labels are kept as written,
/// the way a stored row keeps a label its vocabulary has since dropped.
/// </summary>
public sealed class StoryImport
{
    private readonly Dictionary<string, Guid> _characters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _locations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _props = new(StringComparer.Ordinal);

    private StoryImport()
    {
    }

    public static StoryRecord ToStory(TheplotFile file) => new StoryImport().Read(file);

    private StoryRecord Read(TheplotFile file)
    {
        var title = OutlineText.StoryTitle(file.Title);
        var vocabulary = In("The vocabulary", () => Vocabulary(file.Vocabulary));

        var characters = new List<CharacterRecord>();
        foreach (var entry in file.Characters)
        {
            var character = In($"Character “{entry.Name}”", () => new CharacterRecord
            {
                Id = Guid.CreateVersion7(),
                Name = LibraryNames.Check(characters.Select(c => (c.Id, c.Name)), entry.Name, "character"),
                Kind = Label(entry.Kind)!,
                Hue = LibraryNames.Hue(entry.Hue),
            });
            Key(_characters, entry.Id, character.Id, "characters");
            characters.Add(character);
        }

        var locations = new List<LocationRecord>();
        foreach (var entry in file.Locations)
        {
            var location = In($"Location “{entry.Name}”", () => new LocationRecord
            {
                Id = Guid.CreateVersion7(),
                Name = LibraryNames.Check(locations.Select(l => (l.Id, l.Name)), entry.Name, "location"),
                Setting = entry.Setting,
                Hue = LibraryNames.Hue(entry.Hue),
            });
            Key(_locations, entry.Id, location.Id, "locations");
            locations.Add(location);
        }

        var props = new List<PropRecord>();
        foreach (var entry in file.Props)
        {
            var prop = In($"Prop “{entry.Name}”", () => new PropRecord
            {
                Id = Guid.CreateVersion7(),
                Name = LibraryNames.Check(props.Select(p => (p.Id, p.Name)), entry.Name, "prop"),
            });
            Key(_props, entry.Id, prop.Id, "props");
            props.Add(prop);
        }

        var references = new StoryReferences
        {
            CharacterIds = _characters.Values.ToHashSet(),
            LocationIds = _locations.Values.ToHashSet(),
            PropIds = _props.Values.ToHashSet(),
            TimesOfDay = VocabularyLabels.TimesOfDay(vocabulary),
            Transitions = VocabularyLabels.Transitions(vocabulary),
            Extensions = VocabularyLabels.Extensions(vocabulary),
        };
        var containers = new List<ContainerRecord>();
        foreach (var entry in file.Containers)
        {
            var label = In($"Container {containers.Count + 1}", () => OutlineText.ContainerLabel(entry.Label));
            var scenes = new List<SceneRecord>();
            foreach (var scene in entry.Scenes)
            {
                scenes.Add(In($"“{label}”, scene {scenes.Count + 1}", () => new SceneRecord
                {
                    Position = scenes.Count,
                    Title = OutlineText.SceneTitle(scene.Title),
                    Elements = Elements(scene.Elements, references),
                }));
            }

            containers.Add(new ContainerRecord { Position = containers.Count, Label = label, Scenes = scenes });
        }

        if (containers.All(c => c.Scenes.Count == 0))
        {
            throw StoryErrors.Invalid("A story needs at least one scene.");
        }

        return new StoryRecord
        {
            Title = title,
            Vocabulary = vocabulary,
            Characters = characters,
            Locations = locations,
            Props = props,
            Containers = containers,
        };
    }

    /// <summary>A label the defaults have gained since the export is offered already, so it is dropped.</summary>
    private static StoryVocabularyRecord Vocabulary(FileVocabulary vocabulary)
    {
        var message = new StoryVocabulary();
        message.TimesOfDay.AddRange(Additions(vocabulary.TimesOfDay, DefaultVocabulary.TimesOfDay));
        message.Transitions.AddRange(Additions(vocabulary.Transitions, DefaultVocabulary.Transitions));
        message.Extensions.AddRange(Additions(vocabulary.Extensions, DefaultVocabulary.Extensions));
        message.CharacterKinds.AddRange(vocabulary.CharacterKinds
            .Where(k => !DefaultVocabulary.CharacterKinds.Any(d => LibraryNames.Same(d.Label, k.Label)))
            .Select(k => new CharacterKind { Label = k.Label, AvatarShape = StoryMessages.ToMessage(k.AvatarShape) }));
        return VocabularyLabels.Validate(message);
    }

    private static IEnumerable<string> Additions(List<string> labels, IReadOnlyList<string> defaults) =>
        labels.Where(l => !defaults.Any(d => LibraryNames.Same(d, l)));

    private List<SceneElementRecord> Elements(List<FileElement> rows, StoryReferences references)
    {
        var messages = new RepeatedField<SceneElement>();
        var asWritten = new Dictionary<Guid, SceneElementRecord>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var number = index + 1;
            var id = Guid.CreateVersion7();
            var message = new SceneElement { Id = id.ToString(), Type = StoryMessages.ToMessage(row.Type) };
            if (row.Text is not null)
            {
                message.Text = row.Text;
            }

            if (row.Location is not null)
            {
                message.LocationId = Reference(_locations, row.Location, number, "location");
            }

            if (Label(row.TimeOfDay) is { } timeOfDay)
            {
                message.TimeOfDay = timeOfDay;
            }

            if (row.Speaker is not null)
            {
                message.SpeakerId = Reference(_characters, row.Speaker, number, "character");
            }

            if (Label(row.Extension) is { } extension)
            {
                message.Extension = extension;
            }

            if (row.Parenthetical is not null)
            {
                message.Parenthetical = row.Parenthetical;
            }

            if (Label(row.Transition) is { } transition)
            {
                message.Transition = transition;
            }

            message.Mentions.AddRange((row.Mentions ?? []).Select(m => Mention(m, number)));
            messages.Add(message);
            asWritten[id] = new SceneElementRecord
            {
                TimeOfDay = row.TimeOfDay,
                Extension = row.Extension,
                Transition = row.Transition,
            };
        }

        return SceneValidator.Validate(messages, references, asWritten);
    }

    private SceneElementMention Mention(FileMention mention, int row)
    {
        var message = new SceneElementMention { Offset = mention.Offset, Length = mention.Length, IsTag = mention.Tag };
        if (new[] { mention.Character, mention.Location, mention.Prop }.Count(t => t is not null) > 1)
        {
            throw StoryErrors.Invalid($"Row {row}: a mention points at one character, location or prop, not several.");
        }

        if (mention.Character is not null)
        {
            message.CharacterId = Reference(_characters, mention.Character, row, "character");
        }
        else if (mention.Location is not null)
        {
            message.LocationId = Reference(_locations, mention.Location, row, "location");
        }
        else if (mention.Prop is not null)
        {
            message.PropId = Reference(_props, mention.Prop, row, "prop");
        }

        return message;
    }

    private static string Reference(Dictionary<string, Guid> ids, string key, int row, string noun) =>
        ids.TryGetValue(key, out var id)
            ? id.ToString()
            : throw StoryErrors.Invalid($"Row {row}: the file has no {noun} with the id “{key}”.");

    private static void Key(Dictionary<string, Guid> ids, string key, Guid id, string nouns)
    {
        if (!ids.TryAdd(key, id))
        {
            throw StoryErrors.Invalid($"Two {nouns} in the file share the id “{key}”.");
        }
    }

    private static string? Label(string? label)
    {
        if (label is null)
        {
            return null;
        }

        if (label.Trim().Length == 0)
        {
            throw StoryErrors.Invalid("Type a label first.");
        }

        return label.Length <= VocabularyLabels.MaxLength
            ? label
            : throw StoryErrors.Invalid($"Labels are limited to {VocabularyLabels.MaxLength} characters.");
    }

    /// <summary>Says where in the file a rule was broken. Every refusal is the file's fault, so invalid.</summary>
    private static T In<T>(string where, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.InvalidArgument or StatusCode.AlreadyExists)
        {
            throw StoryErrors.Invalid($"{where} — {exception.Status.Detail}");
        }
    }
}
