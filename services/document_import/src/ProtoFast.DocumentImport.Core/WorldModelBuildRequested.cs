namespace ProtoFast.DocumentImport.Core;

/// <summary>The queue message the API sends once a writer asks for a story's world model to be built.</summary>
public sealed record WorldModelBuildRequested(Guid BuildId, Guid StoryId, string UserId);
