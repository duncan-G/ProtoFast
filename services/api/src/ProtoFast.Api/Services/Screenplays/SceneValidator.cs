using Google.Protobuf.Collections;
using SceneElementMentionRecord = ProtoFast.Data.ThePlot.Entities.SceneElementMention;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;
using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>
/// Checks a scene's elements against the story and returns them as detached records, positioned
/// by their order. Mention ids are assigned on save: the client regenerates its
/// own on every re-link, so they carry nothing worth keeping.
/// </summary>
public static class SceneValidator
{
    public const int ParentheticalMaxLength = 255;

    /// <param name="current">The scene's stored elements by id, so a label it already uses stays valid.</param>
    public static List<SceneElementRecord> Validate(
        RepeatedField<SceneElement> elements,
        StoryReferences story,
        IReadOnlyDictionary<Guid, SceneElementRecord> current)
    {
        var ids = new HashSet<Guid>();
        var records = new List<SceneElementRecord>(elements.Count);
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var row = index + 1;
            if (!Guid.TryParse(element.Id, out var id) || id == Guid.Empty)
            {
                throw StoryErrors.Invalid($"Row {row} has no valid id.");
            }

            if (!ids.Add(id))
            {
                throw StoryErrors.Invalid($"Row {row} repeats another row’s id.");
            }

            var type = StoryMessages.FromMessage(element.Type)
                       ?? throw StoryErrors.Invalid($"Row {row} has no type.");
            current.TryGetValue(id, out var stored);
            var record = new SceneElementRecord { Id = id, Position = index, Type = type };
            var rows = Plural(type);

            switch (type)
            {
                case SceneElementTypeRecord.Heading:
                    Forbid(element.HasText, row, rows, "text");
                    Forbid(element.HasSpeakerId, row, rows, "a speaker");
                    Forbid(element.HasParenthetical, row, rows, "a parenthetical");
                    Forbid(element.HasTransition, row, rows, "a transition");
                    record.LocationId = element.HasLocationId
                        ? Reference(element.LocationId, story.LocationIds, row, "location")
                        : null;
                    record.TimeOfDay = VocabularyLabels.Pick(
                        element.HasTimeOfDay ? element.TimeOfDay : null,
                        story.TimesOfDay,
                        stored?.TimeOfDay,
                        "time of day");
                    break;

                case SceneElementTypeRecord.Transition:
                    Forbid(element.HasText, row, rows, "text");
                    Forbid(element.HasLocationId, row, rows, "a location");
                    Forbid(element.HasTimeOfDay, row, rows, "a time of day");
                    Forbid(element.HasSpeakerId, row, rows, "a speaker");
                    Forbid(element.HasParenthetical, row, rows, "a parenthetical");
                    record.Transition = VocabularyLabels.Pick(
                        element.HasTransition ? element.Transition : null,
                        story.Transitions,
                        stored?.Transition,
                        "transition");
                    break;

                case SceneElementTypeRecord.Dialogue:
                    Forbid(element.HasLocationId, row, rows, "a location");
                    Forbid(element.HasTimeOfDay, row, rows, "a time of day");
                    Forbid(element.HasTransition, row, rows, "a transition");
                    record.Text = element.Text;
                    record.SpeakerId = element.HasSpeakerId
                        ? Reference(element.SpeakerId, story.CharacterIds, row, "character")
                        : null;
                    record.Parenthetical = element.HasParenthetical ? Parenthetical(element.Parenthetical) : null;
                    break;

                default:
                    Forbid(element.HasLocationId, row, rows, "a location");
                    Forbid(element.HasTimeOfDay, row, rows, "a time of day");
                    Forbid(element.HasSpeakerId, row, rows, "a speaker");
                    Forbid(element.HasParenthetical, row, rows, "a parenthetical");
                    Forbid(element.HasTransition, row, rows, "a transition");
                    record.Text = element.Text;
                    break;
            }

            if (record.Text is null)
            {
                Forbid(element.Mentions.Count > 0, row, rows, "mentions");
            }
            else
            {
                record.Mentions = Mentions(element.Mentions, record.Text, story, row);
            }

            records.Add(record);
        }

        return records;
    }

    private static List<SceneElementMentionRecord> Mentions(
        RepeatedField<SceneElementMention> mentions,
        string text,
        StoryReferences story,
        int row)
    {
        var records = new List<SceneElementMentionRecord>(mentions.Count);
        var end = 0;
        foreach (var mention in mentions.OrderBy(m => m.Offset))
        {
            if (mention.Length < 2)
            {
                throw StoryErrors.Invalid($"Row {row}: a mention needs a name after its @.");
            }

            if (mention.Offset < 0 || (long)mention.Offset + mention.Length > text.Length)
            {
                throw StoryErrors.Invalid($"Row {row}: a mention runs outside the text.");
            }

            if (text[mention.Offset] != '@')
            {
                throw StoryErrors.Invalid($"Row {row}: a mention has to start at an @.");
            }

            if (mention.Offset < end)
            {
                throw StoryErrors.Invalid($"Row {row}: two mentions overlap.");
            }

            end = mention.Offset + mention.Length;
            var record = new SceneElementMentionRecord { Offset = mention.Offset, Length = mention.Length };
            switch (mention.TargetCase)
            {
                case SceneElementMention.TargetOneofCase.CharacterId:
                    record.CharacterId = Reference(mention.CharacterId, story.CharacterIds, row, "character");
                    break;
                case SceneElementMention.TargetOneofCase.LocationId:
                    record.LocationId = Reference(mention.LocationId, story.LocationIds, row, "location");
                    break;
                case SceneElementMention.TargetOneofCase.PropId:
                    record.PropId = Reference(mention.PropId, story.PropIds, row, "prop");
                    break;
                default:
                    throw StoryErrors.Invalid($"Row {row}: a mention has to point at a character, location or prop.");
            }

            records.Add(record);
        }

        return records;
    }

    private static Guid Reference(string id, IReadOnlySet<Guid> ids, int row, string noun) =>
        Guid.TryParse(id, out var parsed) && ids.Contains(parsed)
            ? parsed
            : throw StoryErrors.Invalid($"Row {row}: that {noun} isn’t in this story’s library.");

    private static string? Parenthetical(string parenthetical)
    {
        var trimmed = parenthetical.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        if (trimmed.Length > ParentheticalMaxLength)
        {
            throw StoryErrors.Invalid($"Parentheticals are limited to {ParentheticalMaxLength} characters.");
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    private static void Forbid(bool isSet, int row, string rows, string field)
    {
        if (isSet)
        {
            throw StoryErrors.Invalid($"Row {row}: {rows} can’t have {field}.");
        }
    }

    private static string Plural(SceneElementTypeRecord type) => type switch
    {
        SceneElementTypeRecord.Heading => "headings",
        SceneElementTypeRecord.Action => "action lines",
        SceneElementTypeRecord.Description => "descriptions",
        SceneElementTypeRecord.Narration => "narration lines",
        SceneElementTypeRecord.Dialogue => "dialogue lines",
        _ => "transitions",
    };
}
