namespace ProtoFast.DocumentImport.Screenplay.Drafts;

public sealed record SceneDraft(string Title, IReadOnlyList<SceneElementDraft> Elements);
