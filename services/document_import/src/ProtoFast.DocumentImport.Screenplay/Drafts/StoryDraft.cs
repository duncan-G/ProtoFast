namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <summary>Output of the terminal <c>story</c> stage: the library and the scenes in one document.</summary>
public sealed record StoryDraft(
    string Title,
    IReadOnlyList<CharacterDraft> Characters,
    IReadOnlyList<LocationDraft> Locations,
    IReadOnlyList<PropDraft> Props,
    IReadOnlyList<ContainerDraft> Containers,
    StoryVocabularyDraft? Vocabulary = null);
