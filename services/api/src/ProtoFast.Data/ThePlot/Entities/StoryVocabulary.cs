namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// The labels a story adds to <see cref="DefaultVocabulary"/>. Stored as JSON on the story, so it
/// loads with it.
/// </summary>
public sealed class StoryVocabulary
{
    public List<string> TimesOfDay { get; set; } = [];

    public List<string> Transitions { get; set; } = [];

    public List<CharacterKind> CharacterKinds { get; set; } = [];
}
