namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <param name="Label">"Act I", "Part One", "Episode 3".</param>
public sealed record ContainerDraft(string Label, IReadOnlyList<SceneDraft> Scenes);
