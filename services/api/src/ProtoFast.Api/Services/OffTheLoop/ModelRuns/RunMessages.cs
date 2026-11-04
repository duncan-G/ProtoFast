using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Entities;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

public static class RunMessages
{
    public static Run ToMessage(ModelRun run) => new()
    {
        Id = run.Id.ToString(),
        Kind = ToMessage(run.Kind),
        Label = run.Label,
        Status = ToMessage(run.Status),
        Error = run.Error ?? "",
        CompanyId = run.CompanyId?.ToString() ?? "",
        ApplicationId = run.ApplicationId?.ToString() ?? "",
        NoteId = run.NoteId?.ToString() ?? "",
        Attempt = run.Attempts,
        CreatedUnixSeconds = LoopText.UnixSeconds(run.DateCreated),
        StartedUnixSeconds = run.StartedAt is { } started ? LoopText.UnixSeconds(started) : 0,
        FinishedUnixSeconds = run.FinishedAt is { } finished ? LoopText.UnixSeconds(finished) : 0,
    };

    public static bool IsFinished(ModelRunStatus status) =>
        status is ModelRunStatus.Succeeded or ModelRunStatus.Failed or ModelRunStatus.Cancelled;

    private static RunKind ToMessage(ModelRunKind kind) => kind switch
    {
        ModelRunKind.ResearchCompany => RunKind.ResearchCompany,
        ModelRunKind.ReadPosting => RunKind.ReadPosting,
        ModelRunKind.Write => RunKind.Write,
        ModelRunKind.ReadResume => RunKind.ReadResume,
        ModelRunKind.TailorResume => RunKind.TailorResume,
        _ => RunKind.Unspecified,
    };

    private static RunStatus ToMessage(ModelRunStatus status) => status switch
    {
        ModelRunStatus.Queued => RunStatus.Queued,
        ModelRunStatus.Running => RunStatus.Running,
        ModelRunStatus.Succeeded => RunStatus.Succeeded,
        ModelRunStatus.Failed => RunStatus.Failed,
        ModelRunStatus.Cancelled => RunStatus.Cancelled,
        _ => RunStatus.Unspecified,
    };
}
