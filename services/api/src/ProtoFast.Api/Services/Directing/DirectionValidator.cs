using Google.Protobuf.Collections;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot.Entities;
using CueRecord = ProtoFast.Data.ThePlot.Entities.Cue;
using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;
using FramePropRecord = ProtoFast.Data.ThePlot.Entities.FrameProp;
using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using SceneLocationStateRecord = ProtoFast.Data.ThePlot.Entities.SceneLocationState;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;
using ScenePropStateRecord = ProtoFast.Data.ThePlot.Entities.ScenePropState;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Checks a director layer against its scene and story and returns it as detached rows. Rows are
/// known by their place in the scene (a cue by its line, a frame by its first row, a look by its
/// character, a state by its prop); ids are the server's and only come back for display.
/// </summary>
public static class DirectionValidator
{
    public const int FieldMaxLength = 255;

    public const int BeatMaxLength = 2000;

    public static DirectionDraft Validate(SceneDirection message, DirectionReferences references)
    {
        if (message.HasStoryDay && message.StoryDay < 1)
        {
            throw StoryErrors.Invalid("Story days start at 1.");
        }

        return new DirectionDraft
        {
            StoryDay = message.HasStoryDay ? message.StoryDay : null,
            Shot = OptionalLabel(message.HasShot ? message.Shot : null, DirectionVocabulary.Shots, "The scene", "shot"),
            Angle = OptionalLabel(message.HasAngle ? message.Angle : null, DirectionVocabulary.Angles, "The scene", "camera angle"),
            Movement = OptionalLabel(message.HasMovement ? message.Movement : null, DirectionVocabulary.Movements, "The scene", "camera movement"),
            Cues = Cues(message.Cues, references),
            Frames = Frames(message.Frames, references),
            Appearances = Appearances(message.Appearances, references),
            PropStates = PropStates(message.PropStates, references),
            LocationStates = LocationStates(message.LocationStates, references),
        };
    }

    /// <summary>The performance keeps the author's spelling; its tags have to be in the story's vocabulary.</summary>
    public static string Performance(string performance, IReadOnlyList<string> deliveryTags, string where)
    {
        var trimmed = Performances.Collapse(performance);
        if (trimmed.Length > Performances.MaxLength)
        {
            throw StoryErrors.Invalid($"{where}: the performance is limited to {Performances.MaxLength} characters.");
        }

        var tags = Performances.Tags(trimmed);
        if (tags.Count > Performances.MaxTags)
        {
            throw StoryErrors.Invalid($"{where}: a line takes at most {Performances.MaxTags} delivery tags.");
        }

        foreach (var tag in tags)
        {
            if (!deliveryTags.Any(t => LibraryNames.Same(t, tag)))
            {
                throw StoryErrors.Invalid($"{where}: “{tag}” isn’t a delivery tag in this story.");
            }
        }

        return trimmed;
    }

    private static List<CueRecord> Cues(RepeatedField<Cue> cues, DirectionReferences references)
    {
        var records = new List<CueRecord>(cues.Count);
        var elements = new HashSet<Guid>();
        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            var where = $"Cue {index + 1}";
            var elementId = Element(cue.ElementId, references, where);
            var element = references.Elements[elementId];
            if (!Spoken(element.Type))
            {
                throw StoryErrors.Invalid($"{where}: only dialogue, narration and action lines take a cue.");
            }

            Guid? narratorId = null;
            if (cue.HasNarratorId)
            {
                if (element.Type == SceneElementTypeRecord.Dialogue)
                {
                    throw StoryErrors.Invalid($"{where}: a dialogue line is spoken by its character, not a narrator.");
                }

                narratorId = Reference(cue.NarratorId, references.NarratorIds, where, "narrator");
            }

            if (!elements.Add(elementId))
            {
                throw StoryErrors.Invalid($"{where}: that line already has a cue.");
            }

            records.Add(new CueRecord
            {
                ElementId = elementId,
                Performance = Performance(cue.Performance, references.DeliveryTags, where),
                GapBeforeMs = cue.GapBeforeMs,
                Interrupted = cue.Interrupted,
                NarratorId = narratorId,
            });
        }

        return records;
    }

    /// <summary>The lines that are read aloud: dialogue by its speaker, narration and action by a narrator.</summary>
    public static bool Spoken(SceneElementTypeRecord type) =>
        type is SceneElementTypeRecord.Dialogue or SceneElementTypeRecord.Narration or SceneElementTypeRecord.Action;

    private static List<FrameRecord> Frames(RepeatedField<Frame> frames, DirectionReferences references)
    {
        var records = new List<(FrameRecord Record, int First, int Last)>(frames.Count);
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var where = $"Frame {index + 1}";
            var first = references.Elements[Element(frame.FirstElementId, references, where)];
            var last = references.Elements[Element(frame.LastElementId, references, where)];
            if (first.Position > last.Position)
            {
                throw StoryErrors.Invalid($"{where}: its last row comes before its first.");
            }

            if (frame.HasHoldMs && frame.HoldMs <= 0)
            {
                throw StoryErrors.Invalid($"{where}: a hold has to be longer than nothing.");
            }

            var record = new FrameRecord
            {
                FirstElementId = first.Id,
                LastElementId = last.Id,
                LocationId = frame.HasLocationId ? Reference(frame.LocationId, references.Story.LocationIds, where, "location") : null,
                TimeOfDay = VocabularyLabels.Pick(
                    frame.HasTimeOfDay ? frame.TimeOfDay : null, references.Story.TimesOfDay, current: null, "time of day"),
                Beat = Text(frame.Beat, BeatMaxLength, where, "beat"),
                Shot = OptionalLabel(frame.HasShot ? frame.Shot : null, DirectionVocabulary.Shots, where, "shot"),
                Angle = OptionalLabel(frame.HasAngle ? frame.Angle : null, DirectionVocabulary.Angles, where, "camera angle"),
                Movement = OptionalLabel(frame.HasMovement ? frame.Movement : null, DirectionVocabulary.Movements, where, "camera movement"),
                LocationChange = Text(frame.LocationChange, BeatMaxLength, where, "location change"),
                HoldMs = frame.HasHoldMs ? frame.HoldMs : null,
                Placements = Placements(frame.Placements, references, where),
                Props = FrameProps(frame.Props, references, where),
            };
            records.Add((record, first.Position, last.Position));
        }

        records.Sort((a, b) => a.First.CompareTo(b.First));
        for (var i = 1; i < records.Count; i++)
        {
            if (records[i].First <= records[i - 1].Last)
            {
                throw StoryErrors.Invalid("Two frames cover the same row.");
            }
        }

        for (var i = 0; i < records.Count; i++)
        {
            records[i].Record.Position = i;
        }

        return records.Select(r => r.Record).ToList();
    }

    private static List<FramePlacement> Placements(RepeatedField<Placement> placements, DirectionReferences references, string where)
    {
        var records = new List<FramePlacement>(placements.Count);
        foreach (var placement in placements)
        {
            var characterId = Reference(placement.CharacterId, references.Story.CharacterIds, where, "character");
            if (records.Any(p => p.CharacterId == characterId))
            {
                throw StoryErrors.Invalid($"{where}: a character is placed twice.");
            }

            records.Add(new FramePlacement
            {
                CharacterId = characterId,
                Screen = placement.Screen.Trim().Length == 0
                    ? ""
                    : Label(placement.Screen, DirectionVocabulary.ScreenSides, where, "screen side"),
                Pose = Text(placement.Pose, FieldMaxLength, where, "pose"),
                Expression = Text(placement.Expression, FieldMaxLength, where, "expression"),
                OnScreen = placement.HasOnScreen ? placement.OnScreen : null,
                WardrobeId = placement.HasWardrobeId ? Wardrobe(placement.WardrobeId, characterId, references, where) : null,
                WardrobeChange = Text(placement.WardrobeChange, BeatMaxLength, where, "wardrobe change"),
                AppearanceChange = Text(placement.AppearanceChange, BeatMaxLength, where, "appearance change"),
            });
        }

        return records;
    }

    private static List<FramePropRecord> FrameProps(RepeatedField<FrameProp> props, DirectionReferences references, string where)
    {
        var records = new List<FramePropRecord>(props.Count);
        foreach (var prop in props)
        {
            var id = Reference(prop.PropId, references.Story.PropIds, where, "prop");
            if (records.All(p => p.PropId != id))
            {
                records.Add(new FramePropRecord { PropId = id, InView = prop.InView });
            }
        }

        return records;
    }

    private static List<SceneAppearanceRecord> Appearances(RepeatedField<SceneAppearance> appearances, DirectionReferences references)
    {
        var records = new List<SceneAppearanceRecord>(appearances.Count);
        for (var index = 0; index < appearances.Count; index++)
        {
            var appearance = appearances[index];
            var where = $"Look {index + 1}";
            var characterId = Reference(appearance.CharacterId, references.Story.CharacterIds, where, "character");
            if (records.Any(a => a.CharacterId == characterId))
            {
                throw StoryErrors.Invalid($"{where}: that character already has a look in this scene.");
            }

            records.Add(new SceneAppearanceRecord
            {
                CharacterId = characterId,
                OnScreen = !appearance.HasOnScreen || appearance.OnScreen,
                WardrobeId = appearance.HasWardrobeId ? Wardrobe(appearance.WardrobeId, characterId, references, where) : null,
                WardrobeChange = Text(appearance.WardrobeChange, BeatMaxLength, where, "wardrobe change"),
                AppearanceChange = Text(appearance.AppearanceChange, BeatMaxLength, where, "appearance change"),
                ChangeReason = Reason(appearance.HasChangeReason ? appearance.ChangeReason : null, where),
                ChangeElementId = appearance.HasChangeElementId ? Element(appearance.ChangeElementId, references, where) : null,
            });
        }

        return records;
    }

    private static List<ScenePropStateRecord> PropStates(RepeatedField<ScenePropState> states, DirectionReferences references)
    {
        var records = new List<ScenePropStateRecord>(states.Count);
        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            var where = $"Prop state {index + 1}";
            var propId = Reference(state.PropId, references.Story.PropIds, where, "prop");
            if (records.Any(p => p.PropId == propId))
            {
                throw StoryErrors.Invalid($"{where}: that prop already has a state in this scene.");
            }

            records.Add(new ScenePropStateRecord
            {
                PropId = propId,
                InView = !state.HasInView || state.InView,
                State = States(state.State, where),
                ChangeReason = Reason(state.HasChangeReason ? state.ChangeReason : null, where),
                ChangeElementId = state.HasChangeElementId ? Element(state.ChangeElementId, references, where) : null,
            });
        }

        return records;
    }

    private static List<SceneLocationStateRecord> LocationStates(
        RepeatedField<SceneLocationState> states, DirectionReferences references)
    {
        var records = new List<SceneLocationStateRecord>(states.Count);
        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            var where = $"Location change {index + 1}";
            var locationId = Reference(state.LocationId, references.Story.LocationIds, where, "location");
            if (records.Any(l => l.LocationId == locationId))
            {
                throw StoryErrors.Invalid($"{where}: that location already has changes in this scene.");
            }

            records.Add(new SceneLocationStateRecord
            {
                LocationId = locationId,
                Change = Text(state.Change, BeatMaxLength, where, "location change"),
            });
        }

        return records;
    }

    private static Guid Wardrobe(string id, Guid characterId, DirectionReferences references, string where)
    {
        references.WardrobesByCharacter.TryGetValue(characterId, out var wardrobes);
        return Guid.TryParse(id, out var parsed) && wardrobes?.Contains(parsed) == true
            ? parsed
            : throw StoryErrors.Invalid($"{where}: that wardrobe isn’t on the character’s sheet.");
    }

    private static string? OptionalLabel(string? label, IReadOnlyList<string> labels, string where, string noun) =>
        string.IsNullOrWhiteSpace(label) ? null : Label(label, labels, where, noun);

    private static Guid Element(string id, DirectionReferences references, string where) =>
        Guid.TryParse(id, out var parsed) && references.Elements.ContainsKey(parsed)
            ? parsed
            : throw StoryErrors.Invalid($"{where}: that row isn’t in this scene.");

    private static Guid Reference(string id, IReadOnlySet<Guid> ids, string where, string noun) =>
        Guid.TryParse(id, out var parsed) && ids.Contains(parsed)
            ? parsed
            : throw StoryErrors.Invalid($"{where}: that {noun} isn’t in this story’s library.");

    private static string Label(string label, IReadOnlyList<string> labels, string where, string noun) =>
        labels.FirstOrDefault(l => LibraryNames.Same(l, label))
        ?? throw StoryErrors.Invalid($"{where}: “{label}” isn’t a {noun}.");

    private static string Text(string text, int max, string where, string noun)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max
            ? trimmed
            : throw StoryErrors.Invalid($"{where}: the {noun} is limited to {max} characters.");
    }

    private static string? Reason(string? reason, string where)
    {
        var trimmed = reason?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : Text(trimmed, BeatMaxLength, where, "change reason");
    }

    private static List<string> States(RepeatedField<string> state, string where) =>
        state.Select(s => Text(s, FieldMaxLength, where, "state")).Where(s => s.Length > 0).Distinct().ToList();
}
