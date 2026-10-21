namespace ProtoFast.DocumentImport.History.Runs;

/// <param name="WriterId">The theplot writer who owns the story the run writes.</param>
/// <param name="OperatorId">The console operator who asked for it.</param>
/// <param name="Request">What the operator asked for; null for a random moment.</param>
/// <param name="BriefJson">The research brief the story was written from, once research is done.</param>
public sealed record StoryRun(
    Guid Id,
    string WriterId,
    string OperatorId,
    string? Request,
    int Scenes,
    StoryRunStatus Status,
    string Message,
    string? Title,
    string? BriefJson,
    Guid? StoryId,
    string? Failure,
    decimal Cost,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public bool IsActive => Status is StoryRunStatus.Queued or StoryRunStatus.Researching or StoryRunStatus.Writing or StoryRunStatus.Saving;
}
