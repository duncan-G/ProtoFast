namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <summary>Output of the <c>library</c> stage.</summary>
public sealed record StoryLibraryDraft(
    string Title,
    IReadOnlyList<CharacterDraft> Characters,
    IReadOnlyList<LocationDraft> Locations,
    IReadOnlyList<PropDraft> Props);
