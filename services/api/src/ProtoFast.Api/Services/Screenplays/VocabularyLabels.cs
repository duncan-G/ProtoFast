using ProtoFast.Data.ThePlot.Entities;
using CharacterKindRecord = ProtoFast.Data.ThePlot.Entities.CharacterKind;
using StoryVocabularyRecord = ProtoFast.Data.ThePlot.Entities.StoryVocabulary;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>Ports the client's <c>labelProblem</c>, wording included.</summary>
public static class VocabularyLabels
{
    public const int MaxLength = 64;

    public static IReadOnlyList<string> TimesOfDay(StoryVocabularyRecord vocabulary) =>
        [.. DefaultVocabulary.TimesOfDay, .. vocabulary.TimesOfDay];

    public static IReadOnlyList<string> Transitions(StoryVocabularyRecord vocabulary) =>
        [.. DefaultVocabulary.Transitions, .. vocabulary.Transitions];

    public static IReadOnlyList<string> CharacterKinds(StoryVocabularyRecord vocabulary) =>
        [.. DefaultVocabulary.CharacterKinds.Select(k => k.Label), .. vocabulary.CharacterKinds.Select(k => k.Label)];

    /// <summary>Times of day and transitions are stored uppercased, as headings print them.</summary>
    public static StoryVocabularyRecord Validate(StoryVocabulary vocabulary)
    {
        var timesOfDay = new List<string>();
        foreach (var label in vocabulary.TimesOfDay)
        {
            timesOfDay.Add(Add(label, [.. DefaultVocabulary.TimesOfDay, .. timesOfDay]).ToUpperInvariant());
        }

        var transitions = new List<string>();
        foreach (var label in vocabulary.Transitions)
        {
            transitions.Add(Add(label, [.. DefaultVocabulary.Transitions, .. transitions]).ToUpperInvariant());
        }

        var kinds = new List<CharacterKindRecord>();
        foreach (var kind in vocabulary.CharacterKinds)
        {
            var label = Add(kind.Label, [.. DefaultVocabulary.CharacterKinds.Select(k => k.Label), .. kinds.Select(k => k.Label)]);
            if (kind.AvatarShape == AvatarShape.Unspecified)
            {
                throw StoryErrors.Invalid($"Pick a shape for “{label}”.");
            }

            kinds.Add(new CharacterKindRecord { Label = label, AvatarShape = StoryMessages.FromMessage(kind.AvatarShape) });
        }

        return new StoryVocabularyRecord { TimesOfDay = timesOfDay, Transitions = transitions, CharacterKinds = kinds };
    }

    /// <summary>
    /// The label as the vocabulary spells it. A label the row or character already had passes even if the
    /// vocabulary has since dropped it, so an old scene stays saveable.
    /// </summary>
    public static string? Pick(string? label, IReadOnlyList<string> labels, string? current, string noun)
    {
        if (label is null || label == current)
        {
            return label;
        }

        return labels.FirstOrDefault(l => LibraryNames.Same(l, label))
               ?? throw StoryErrors.Invalid($"“{label}” isn’t a {noun} in this story.");
    }

    private static string Add(string label, IReadOnlyList<string> taken)
    {
        var trimmed = label.Trim();
        if (trimmed.Length == 0)
        {
            throw StoryErrors.Invalid("Type a label first.");
        }

        if (trimmed.Length > MaxLength)
        {
            throw StoryErrors.Invalid($"Labels are limited to {MaxLength} characters.");
        }

        var clash = taken.FirstOrDefault(l => LibraryNames.Same(l, trimmed));
        return clash is null ? trimmed : throw StoryErrors.AlreadyExists($"“{clash}” is already on the list.");
    }
}
