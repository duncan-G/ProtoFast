using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.Api.Services;

internal static class ImportProgressMessages
{
    /// <summary>
    /// A document with nothing in the ledger is still queued. A finished run whose document is
    /// still listed is mid-commit, so it reads as saving until the document is gone.
    /// </summary>
    public static ImportProgress From(string uploadId, RunProgress? progress, bool documentExists)
    {
        var message = new ImportProgress
        {
            UploadId = uploadId,
            State = progress?.Phase switch
            {
                null => ImportState.Queued,
                RunPhase.Preparing => ImportState.Reading,
                RunPhase.Running => ImportState.Analysing,
                RunPhase.Finishing => ImportState.Saving,
                RunPhase.Finished => documentExists ? ImportState.Saving : ImportState.Done,
                RunPhase.Retrying => ImportState.Retrying,
                RunPhase.Failed => ImportState.Failed,
                _ => ImportState.Unspecified,
            },
            Stage = progress is { Phase: RunPhase.Running, StageId: { } stage } ? stage : "",
            Message = progress?.Message ?? "",
            CostUsdMicros = (long)Math.Round((progress?.Cost ?? 0) * 1_000_000m),
        };

        if (message.State == ImportState.Done)
        {
            message.StoryId = progress?.ResultId ?? "";
        }

        return message;
    }
}
