namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <summary>Output of the <c>scenes</c> stage.</summary>
public sealed record StoryScenesDraft(IReadOnlyList<ContainerDraft> Containers);
