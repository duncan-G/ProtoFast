namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>A position in the story: container, scene, then beat. A scene's opening sorts before its first beat.</summary>
public readonly record struct StoryTime(int Container, int Scene, int Beat) : IComparable<StoryTime>
{
    public static StoryTime SceneStart(int container, int scene) => new(container, scene, -1);

    public static StoryTime SceneEnd(int container, int scene) => new(container, scene, int.MaxValue);

    public int CompareTo(StoryTime other)
    {
        var byContainer = Container.CompareTo(other.Container);
        if (byContainer != 0)
        {
            return byContainer;
        }

        var byScene = Scene.CompareTo(other.Scene);
        return byScene != 0 ? byScene : Beat.CompareTo(other.Beat);
    }

    public static bool operator <(StoryTime left, StoryTime right) => left.CompareTo(right) < 0;

    public static bool operator >(StoryTime left, StoryTime right) => left.CompareTo(right) > 0;

    public static bool operator <=(StoryTime left, StoryTime right) => left.CompareTo(right) <= 0;

    public static bool operator >=(StoryTime left, StoryTime right) => left.CompareTo(right) >= 0;
}
