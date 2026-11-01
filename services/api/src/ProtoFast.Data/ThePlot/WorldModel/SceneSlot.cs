namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>Where a scene sits in story time and which location its heading names.</summary>
public sealed record SceneSlot(Guid SceneId, Guid? LocationId, int Container, int Position)
{
    public StoryTime Start => StoryTime.SceneStart(Container, Position);

    public StoryTime End => StoryTime.SceneEnd(Container, Position);
}
