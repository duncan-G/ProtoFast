using AcousticsRecord = ProtoFast.Data.ThePlot.Entities.Acoustics;
using AvatarShapeRecord = ProtoFast.Data.ThePlot.Entities.AvatarShape;
using CharacterIdentityRecord = ProtoFast.Data.ThePlot.Entities.CharacterIdentity;
using CharacterRecord = ProtoFast.Data.ThePlot.Entities.Character;
using CharacterWardrobeRecord = ProtoFast.Data.ThePlot.Entities.CharacterWardrobe;
using LocationAppearanceRecord = ProtoFast.Data.ThePlot.Entities.LocationAppearance;
using ContainerRecord = ProtoFast.Data.ThePlot.Entities.Container;
using LocationRecord = ProtoFast.Data.ThePlot.Entities.Location;
using LocationSettingRecord = ProtoFast.Data.ThePlot.Entities.LocationSetting;
using NarratorRecord = ProtoFast.Data.ThePlot.Entities.Narrator;
using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using StoryRecord = ProtoFast.Data.ThePlot.Entities.Story;
using StoryVocabularyRecord = ProtoFast.Data.ThePlot.Entities.StoryVocabulary;
using VoiceProfileRecord = ProtoFast.Data.ThePlot.Entities.VoiceProfile;

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
        message.Narrators.AddRange(story.Narrators.OrderBy(n => n.Position).ThenBy(n => n.Id).Select(ToMessage));
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
        message.Extensions.AddRange(vocabulary.Extensions);
        message.DeliveryTags.AddRange(vocabulary.DeliveryTags);
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

        if (element.Extension is not null)
        {
            message.Extension = element.Extension;
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
            IsTag = mention.IsTag,
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

    public static Character ToMessage(CharacterRecord character)
    {
        var message = new Character
        {
            Id = character.Id.ToString(),
            StoryId = character.StoryId.ToString(),
            Name = character.Name,
            Kind = character.Kind,
            Hue = character.Hue,
        };
        if (character.Description is not null)
        {
            message.Description = character.Description;
        }

        if (character.Appearance is { } appearance)
        {
            message.Appearance = ToMessage(appearance);
        }

        if (character.Voice is { } voice)
        {
            message.Voice = ToMessage(voice);
        }

        message.Wardrobes.AddRange(character.Wardrobes.OrderBy(w => w.Position).ThenBy(w => w.Id).Select(ToMessage));
        return message;
    }

    public static CharacterIdentity ToMessage(CharacterIdentityRecord identity)
    {
        var message = new CharacterIdentity
        {
            Age = identity.Age,
            // Sheets written before gender existed have no such key in their JSON.
            Gender = identity.Gender ?? "",
            Build = identity.Build,
            Face = identity.Face,
            Hair = identity.Hair,
            Skin = identity.Skin,
        };
        message.Marks.AddRange(identity.Marks);
        return message;
    }

    public static Narrator ToMessage(NarratorRecord narrator) => new()
    {
        Id = narrator.Id.ToString(),
        Name = narrator.Name,
        Voice = ToMessage(narrator.Voice),
    };

    public static VoiceProfile ToMessage(VoiceProfileRecord voice) => new()
    {
        Description = voice.Description,
        Baseline = voice.Baseline,
        VoiceId = voice.VoiceId ?? "",
        // Voices cast before their names were kept have no such keys in their JSON.
        VoiceName = voice.VoiceName ?? "",
        PreviewUrl = voice.PreviewUrl ?? "",
    };

    public static Wardrobe ToMessage(CharacterWardrobeRecord wardrobe) => new()
    {
        Id = wardrobe.Id.ToString(),
        Label = wardrobe.Label,
        Description = wardrobe.Description,
    };

    public static Location ToMessage(LocationRecord location)
    {
        var message = new Location
        {
            Id = location.Id.ToString(),
            StoryId = location.StoryId.ToString(),
            Name = location.Name,
            Setting = ToMessage(location.Setting),
            Hue = location.Hue,
        };
        if (location.Description is not null)
        {
            message.Description = location.Description;
        }

        if (location.Appearance is { } appearance)
        {
            message.Appearance = ToMessage(appearance);
        }

        if (location.Acoustics is { } acoustics)
        {
            message.Acoustics = new Acoustics { Space = acoustics.Space, Ambience = acoustics.Ambience };
        }

        return message;
    }

    public static LocationAppearance ToMessage(LocationAppearanceRecord appearance)
    {
        var message = new LocationAppearance { Layout = appearance.Layout, KeyFeatures = appearance.KeyFeatures };
        message.Lighting.AddRange(appearance.Lighting.Select(l => new LocationLighting
        {
            TimeOfDay = l.TimeOfDay,
            Description = l.Description,
        }));
        return message;
    }

    public static Prop ToMessage(PropRecord prop)
    {
        var message = new Prop
        {
            Id = prop.Id.ToString(),
            StoryId = prop.StoryId.ToString(),
            Name = prop.Name,
        };
        if (prop.Description is not null)
        {
            message.Description = prop.Description;
        }

        return message;
    }

    public static CharacterIdentityRecord FromMessage(CharacterIdentity identity) => new()
    {
        Age = SheetText.Field(identity.Age, "age"),
        Gender = SheetText.Field(identity.Gender, "gender"),
        Build = SheetText.Field(identity.Build, "build"),
        Face = SheetText.Field(identity.Face, "face"),
        Hair = SheetText.Field(identity.Hair, "hair"),
        Skin = SheetText.Field(identity.Skin, "skin"),
        Marks = SheetText.List(identity.Marks, "mark"),
    };

    public static VoiceProfileRecord FromMessage(VoiceProfile voice) => new()
    {
        Description = SheetText.Field(voice.Description, "voice"),
        Baseline = SheetText.Field(voice.Baseline, "voice baseline"),
        VoiceId = SheetText.Field(voice.VoiceId, "voice id") is { Length: > 0 } id ? id : null,
        VoiceName = SheetText.Field(voice.VoiceName, "voice name"),
        PreviewUrl = SheetText.Field(voice.PreviewUrl, "voice preview"),
    };

    public static LocationAppearanceRecord FromMessage(LocationAppearance appearance, IReadOnlyList<string> timesOfDay) => new()
    {
        Layout = SheetText.Field(appearance.Layout, "layout"),
        KeyFeatures = SheetText.Field(appearance.KeyFeatures, "key features"),
        Lighting = appearance.Lighting
            .Select(l => new ProtoFast.Data.ThePlot.Entities.LocationLighting
            {
                TimeOfDay = VocabularyLabels.Pick(l.TimeOfDay, timesOfDay, current: null, "time of day")!,
                Description = SheetText.Field(l.Description, "lighting"),
            })
            .ToList(),
    };

    public static AcousticsRecord FromMessage(Acoustics acoustics) => new()
    {
        Space = SheetText.Field(acoustics.Space, "acoustic space"),
        Ambience = SheetText.Field(acoustics.Ambience, "ambience"),
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
