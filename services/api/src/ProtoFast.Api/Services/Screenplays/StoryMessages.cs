using AvatarShapeRecord = ProtoFast.Data.ThePlot.Entities.AvatarShape;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using ContainerRecord = ProtoFast.Data.ThePlot.Entities.Container;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using LocationSettingRecord = ProtoFast.Data.ThePlot.Entities.LocationSetting;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;
using StoryVocabularyRecord = ProtoFast.Data.ThePlot.Entities.StoryVocabulary;

namespace ProtoFast.Api.Services.Screenplays;

public static class StoryMessages
{
    public static StorySummary ToSummary(StoryRecord story) => new()
    {
        Id = story.Id.ToString(),
        Title = story.Title,
        CreatedUnixSeconds = UnixSeconds(story.DateCreated),
        LastModifiedUnixSeconds = UnixSeconds(story.DateLastModified),
    };

    /// <param name="openings">Each scene's first heading.</param>
    public static Story ToMessage(StoryRecord story, IReadOnlyDictionary<Guid, SceneElementRecord> openings)
    {
        var message = new Story
        {
            Id = story.Id.ToString(),
            Title = story.Title,
            Vocabulary = ToMessage(story.Vocabulary),
        };
        message.Containers.AddRange(story.Containers.Select(c => ToMessage(c, openings)));
        message.Characters.AddRange(story.Characters.Select(ToMessage));
        message.Locations.AddRange(story.Locations.Select(ToMessage));
        message.Props.AddRange(story.Props.Select(ToMessage));
        return message;
    }

    public static StoryVocabulary ToMessage(StoryVocabularyRecord vocabulary)
    {
        var message = new StoryVocabulary();
        message.TimesOfDay.AddRange(vocabulary.TimesOfDay);
        message.Transitions.AddRange(vocabulary.Transitions);
        message.CharacterKinds.AddRange(vocabulary.CharacterKinds.Select(k => new CharacterKind
        {
            Label = k.Label,
            AvatarShape = ToMessage(k.AvatarShape),
        }));
        return message;
    }

    public static Container ToMessage(ContainerRecord container, IReadOnlyDictionary<Guid, SceneElementRecord> openings)
    {
        var message = new Container
        {
            Id = container.Id.ToString(),
            StoryId = container.StoryId.ToString(),
            Position = container.Position,
            Label = container.Label,
        };
        message.Scenes.AddRange(container.Scenes.Select(scene =>
        {
            var summary = new SceneSummary
            {
                Id = scene.Id.ToString(),
                ContainerId = scene.ContainerId.ToString(),
                Position = scene.Position,
                Title = scene.Title,
            };
            if (openings.TryGetValue(scene.Id, out var opening))
            {
                if (opening.LocationId is { } locationId)
                {
                    summary.OpeningLocationId = locationId.ToString();
                }

                if (opening.TimeOfDay is { } timeOfDay)
                {
                    summary.OpeningTimeOfDay = timeOfDay;
                }
            }

            return summary;
        }));
        return message;
    }

    public static Scene ToMessage(SceneRecord scene)
    {
        var message = new Scene
        {
            Id = scene.Id.ToString(),
            ContainerId = scene.ContainerId.ToString(),
            Position = scene.Position,
            Title = scene.Title,
        };
        message.Elements.AddRange(scene.Elements.OrderBy(e => e.Position).ThenBy(e => e.Id).Select(ToMessage));
        return message;
    }

    public static SceneElement ToMessage(SceneElementRecord element)
    {
        var message = new SceneElement
        {
            Id = element.Id.ToString(),
            SceneId = element.SceneId.ToString(),
            Position = element.Position,
            Type = ToMessage(element.Type),
        };
        if (element.Text is not null)
        {
            message.Text = element.Text;
        }

        if (element.LocationId is { } locationId)
        {
            message.LocationId = locationId.ToString();
        }

        if (element.TimeOfDay is not null)
        {
            message.TimeOfDay = element.TimeOfDay;
        }

        if (element.SpeakerId is { } speakerId)
        {
            message.SpeakerId = speakerId.ToString();
        }

        if (element.Parenthetical is not null)
        {
            message.Parenthetical = element.Parenthetical;
        }

        if (element.Transition is not null)
        {
            message.Transition = element.Transition;
        }

        message.Mentions.AddRange(element.Mentions.OrderBy(m => m.Offset).Select(ToMessage));
        return message;
    }

    public static SceneElementMention ToMessage(SceneElementMentionRecord mention)
    {
        var message = new SceneElementMention
        {
            Id = mention.Id.ToString(),
            Offset = mention.Offset,
            Length = mention.Length,
        };
        if (mention.CharacterId is { } characterId)
        {
            message.CharacterId = characterId.ToString();
        }
        else if (mention.LocationId is { } locationId)
        {
            message.LocationId = locationId.ToString();
        }
        else if (mention.PropId is { } propId)
        {
            message.PropId = propId.ToString();
        }

        return message;
    }

    public static Character ToMessage(CharacterRecord character) => new()
    {
        Id = character.Id.ToString(),
        StoryId = character.StoryId.ToString(),
        Name = character.Name,
        Kind = character.Kind,
        Hue = character.Hue,
    };

    public static Location ToMessage(LocationRecord location) => new()
    {
        Id = location.Id.ToString(),
        StoryId = location.StoryId.ToString(),
        Name = location.Name,
        Setting = ToMessage(location.Setting),
        Hue = location.Hue,
    };

    public static Prop ToMessage(PropRecord prop) => new()
    {
        Id = prop.Id.ToString(),
        StoryId = prop.StoryId.ToString(),
        Name = prop.Name,
    };

    public static SceneElementType ToMessage(SceneElementTypeRecord type) => type switch
    {
        SceneElementTypeRecord.Heading => SceneElementType.Heading,
        SceneElementTypeRecord.Action => SceneElementType.Action,
        SceneElementTypeRecord.Description => SceneElementType.Description,
        SceneElementTypeRecord.Narration => SceneElementType.Narration,
        SceneElementTypeRecord.Dialogue => SceneElementType.Dialogue,
        SceneElementTypeRecord.Transition => SceneElementType.Transition,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static SceneElementTypeRecord? FromMessage(SceneElementType type) => type switch
    {
        SceneElementType.Heading => SceneElementTypeRecord.Heading,
        SceneElementType.Action => SceneElementTypeRecord.Action,
        SceneElementType.Description => SceneElementTypeRecord.Description,
        SceneElementType.Narration => SceneElementTypeRecord.Narration,
        SceneElementType.Dialogue => SceneElementTypeRecord.Dialogue,
        SceneElementType.Transition => SceneElementTypeRecord.Transition,
        _ => null,
    };

    public static LocationSetting ToMessage(LocationSettingRecord setting) => setting switch
    {
        LocationSettingRecord.Interior => LocationSetting.Interior,
        LocationSettingRecord.Exterior => LocationSetting.Exterior,
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
    };

    public static LocationSettingRecord FromMessage(LocationSetting setting) => setting switch
    {
        LocationSetting.Interior => LocationSettingRecord.Interior,
        LocationSetting.Exterior => LocationSettingRecord.Exterior,
        _ => throw StoryErrors.Invalid("Pick interior or exterior."),
    };

    // Proto enums reserve 0 for "unspecified", so the members sit one above the entity's.
    public static AvatarShape ToMessage(AvatarShapeRecord shape) => (AvatarShape)((int)shape + 1);

    public static AvatarShapeRecord FromMessage(AvatarShape shape) =>
        shape is > AvatarShape.Unspecified and <= AvatarShape.Shield
            ? (AvatarShapeRecord)((int)shape - 1)
            : throw StoryErrors.Invalid("Pick an avatar shape.");

    private static long UnixSeconds(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
}
