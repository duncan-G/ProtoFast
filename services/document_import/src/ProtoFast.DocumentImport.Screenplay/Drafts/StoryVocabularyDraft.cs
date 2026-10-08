namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <summary>
/// The labels the manuscript uses beyond the defaults. The writer also adds any label an element or
/// character uses without declaring it here.
/// </summary>
public sealed record StoryVocabularyDraft(
    IReadOnlyList<string>? TimesOfDay,
    IReadOnlyList<string>? Transitions,
    IReadOnlyList<CharacterKindDraft>? CharacterKinds);
