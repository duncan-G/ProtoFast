using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.WorldModels;
using ProtoFast.DocumentImport.History.Runs;

namespace ProtoFast.Api.Services.Admin;

internal static class HistoryAdminMessages
{
    public static HistoryStoryRun From(StoryRun run) => new()
    {
        Id = run.Id.ToString(),
        WriterId = run.WriterId,
        OperatorId = run.OperatorId,
        Request = run.Request ?? "",
        Scenes = run.Scenes,
        Status = ToStatus(run.Status),
        Message = run.Message,
        Title = run.Title ?? "",
        StoryId = run.StoryId?.ToString() ?? "",
        Failure = run.Failure ?? "",
        CostUsdMicros = WorldModelMessages.Micros(run.Cost),
        Attempts = run.Attempts,
        CreatedUnixMs = run.CreatedAt.ToUnixTimeMilliseconds(),
        StartedUnixMs = run.StartedAt?.ToUnixTimeMilliseconds() ?? 0,
        FinishedUnixMs = run.FinishedAt?.ToUnixTimeMilliseconds() ?? 0,
    };

    public static HistoryStoryRunEvent From(StoryRunEvent e) => new()
    {
        Seq = e.Seq,
        AtUnixMs = e.At.ToUnixTimeMilliseconds(),
        Kind = e.Kind,
        Summary = e.Summary,
        Detail = e.Detail ?? "",
        CostUsdMicros = WorldModelMessages.Micros(e.Cost),
    };

    private static HistoryStoryRunStatus ToStatus(StoryRunStatus status) => status switch
    {
        StoryRunStatus.Queued => HistoryStoryRunStatus.Queued,
        StoryRunStatus.Researching => HistoryStoryRunStatus.Researching,
        StoryRunStatus.Writing => HistoryStoryRunStatus.Writing,
        StoryRunStatus.Saving => HistoryStoryRunStatus.Saving,
        StoryRunStatus.Finished => HistoryStoryRunStatus.Finished,
        StoryRunStatus.Failed => HistoryStoryRunStatus.Failed,
        StoryRunStatus.Cancelled => HistoryStoryRunStatus.Cancelled,
        _ => HistoryStoryRunStatus.Unspecified,
    };
}
