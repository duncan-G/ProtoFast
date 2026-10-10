namespace ProtoFast.Api.Services.Jobs;

public static class DirectorJobErrors
{
    public const string JobNotFound = "That job could not be found.";
    public const string NoSteps = "There’s nothing to do.";
    public const string EmptyStep = "One of the steps says nothing to do.";
    public const string TooManySteps = "That’s too much to queue at once.";
    public const string ScenesDiffer = "A job works on one scene at a time.";
}
