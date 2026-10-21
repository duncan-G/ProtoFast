namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>Whether a scene's heading time says it follows straight on from the scene before.</summary>
public static class SceneContinuity
{
    private static readonly string[] FollowOn = ["CONTINUOUS", "MOMENTS LATER", "SAME TIME", "SAME"];

    public static bool FollowsOn(string? timeOfDay) =>
        timeOfDay is { } time && FollowOn.Contains(time.Trim().TrimEnd('.').ToUpperInvariant());
}
