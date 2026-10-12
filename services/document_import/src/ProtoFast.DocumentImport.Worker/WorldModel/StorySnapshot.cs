namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>The story as the build reads it: its library and every scene's beats, in story order.</summary>
public sealed record StorySnapshot(
    Guid StoryId,
    string Title,
    IReadOnlyList<LocationInfo> Locations,
    IReadOnlyList<CharacterInfo> Characters,
    IReadOnlyList<PropInfo> Props,
    IReadOnlyList<SceneInfo> Scenes);
