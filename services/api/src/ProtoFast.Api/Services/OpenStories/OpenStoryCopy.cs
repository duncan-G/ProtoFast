using CharacterKindRecord = ProtoFast.Data.ThePlot.Entities.CharacterKind;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using ContainerRecord = ProtoFast.Data.ThePlot.Entities.Container;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;
using StoryVocabularyRecord = ProtoFast.Data.ThePlot.Entities.StoryVocabulary;

namespace ProtoFast.Api.Services.OpenStories;

/// <summary>
/// A writer's own copy of an open story: its outline, scenes, mentions, library and vocabulary,
/// with every reference pointed at the copy's rows. The world model is left behind; the writer
/// builds their own.
/// </summary>
public static class OpenStoryCopy
{
    public static StoryRecord From(StoryRecord open)
    {
        var characters = open.Characters.ToDictionary(c => c.Id, c => new CharacterRecord { Name = c.Name, Kind = c.Kind, Hue = c.Hue });
        var locations = open.Locations.ToDictionary(l => l.Id, l => new LocationRecord { Name = l.Name, Setting = l.Setting, Hue = l.Hue });
        var props = open.Props.ToDictionary(p => p.Id, p => new PropRecord { Name = p.Name });
        foreach (var location in open.Locations)
        {
            if (location.InsideLocationId is { } inside && locations.TryGetValue(inside, out var outer))
            {
                locations[location.Id].InsideLocation = outer;
            }
        }

        return new StoryRecord
        {
            Title = open.Title,
            SourceStoryId = open.Id,
            Characters = [.. characters.Values],
            Locations = [.. locations.Values],
            Props = [.. props.Values],
            Vocabulary = new StoryVocabularyRecord
            {
                TimesOfDay = [.. open.Vocabulary.TimesOfDay],
                Transitions = [.. open.Vocabulary.Transitions],
                Extensions = [.. open.Vocabulary.Extensions],
                CharacterKinds = [.. open.Vocabulary.CharacterKinds.Select(k => new CharacterKindRecord { Label = k.Label, AvatarShape = k.AvatarShape })],
            },
            Containers = [.. open.Containers.OrderBy(c => c.Position).Select(container => new ContainerRecord
            {
                Label = container.Label,
                Position = container.Position,
                Scenes = [.. container.Scenes.OrderBy(s => s.Position).Select(scene => new SceneRecord
                {
                    Title = scene.Title,
                    Position = scene.Position,
                    Elements = [.. scene.Elements.OrderBy(e => e.Position).Select(element => new SceneElementRecord
                    {
                        Position = element.Position,
                        Type = element.Type,
                        Text = element.Text,
                        Location = Mapped(locations, element.LocationId),
                        TimeOfDay = element.TimeOfDay,
                        Speaker = Mapped(characters, element.SpeakerId),
                        Extension = element.Extension,
                        Parenthetical = element.Parenthetical,
                        Transition = element.Transition,
                        Mentions = [.. element.Mentions.Select(m => Mention(m, characters, locations, props)).OfType<SceneElementMentionRecord>()],
                    })],
                })],
            })],
        };
    }

    private static SceneElementMentionRecord? Mention(
        SceneElementMentionRecord mention,
        Dictionary<Guid, CharacterRecord> characters,
        Dictionary<Guid, LocationRecord> locations,
        Dictionary<Guid, PropRecord> props)
    {
        var copy = new SceneElementMentionRecord
        {
            Character = Mapped(characters, mention.CharacterId),
            Location = Mapped(locations, mention.LocationId),
            Prop = Mapped(props, mention.PropId),
            IsTag = mention.IsTag,
            Offset = mention.Offset,
            Length = mention.Length,
        };
        return copy.Character is null && copy.Location is null && copy.Prop is null ? null : copy;
    }

    private static T? Mapped<T>(Dictionary<Guid, T> copies, Guid? id) where T : class =>
        id is { } key && copies.TryGetValue(key, out var copy) ? copy : null;
}
