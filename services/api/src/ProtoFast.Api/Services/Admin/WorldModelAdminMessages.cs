using System.Linq.Expressions;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.WorldModels;
using WorldModelBuildRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuild;
using WorldModelBuildStatusRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuildStatus;
using WorldModelBuildStepRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuildStep;
using WorldModelStepKindRecord = ProtoFast.Data.ThePlot.Entities.WorldModelStepKind;
using WorldModelStepStatusRecord = ProtoFast.Data.ThePlot.Entities.WorldModelStepStatus;
using ViewPictureRecord = ProtoFast.Data.ThePlot.Entities.ViewPicture;

namespace ProtoFast.Api.Services.Admin;

internal static class WorldModelAdminMessages
{
    /// <summary>A projection, so the story's title and the step count come in the same query.</summary>
    public static readonly Expression<Func<WorldModelBuildRecord, WorldModelBuildHeader>> Header = b => new WorldModelBuildHeader
    {
        Id = b.Id.ToString(),
        StoryId = b.StoryId.ToString(),
        StoryTitle = b.Story.Title,
        OwnerId = b.UserId,
        Status = ToStatus(b.Status),
        RequestedUnixMs = WorldModelMessages.Millis(b.DateCreated),
        StartedUnixMs = WorldModelMessages.Millis(b.StartedAt),
        FinishedUnixMs = WorldModelMessages.Millis(b.FinishedAt),
        ScenesTotal = b.ScenesTotal,
        ScenesDone = b.ScenesDone,
        Steps = b.Steps.Count,
        CostUsdMicros = WorldModelMessages.Micros(b.Cost),
        Message = b.Message ?? "",
        Failure = b.Failure ?? "",
        Deliveries = b.Deliveries,
        TraceId = b.TraceId ?? "",
        PartCount = b.PartCount,
        ObjectCount = b.ObjectCount,
        ObservationCount = b.ObservationCount,
    };

    public static WorldViewPicture Picture(ViewPictureRecord picture) => new()
    {
        Id = picture.Id.ToString(),
        ContentType = picture.ContentType,
        Prompt = picture.Prompt,
        Notes = picture.Notes ?? "",
        CostUsdMicros = picture.CostUsdMicros,
        CreatedUnixMs = WorldModelMessages.Millis(picture.DateLastModified),
    };

    public static WorldModelBuildStep From(WorldModelBuildStepRecord step) => new()
    {
        Id = step.Id.ToString(),
        Sequence = step.Sequence,
        Kind = step.Kind switch
        {
            WorldModelStepKindRecord.Seed => WorldModelStepKind.Seed,
            WorldModelStepKindRecord.ReadScene => WorldModelStepKind.ReadScene,
            WorldModelStepKindRecord.Finish => WorldModelStepKind.Finish,
            WorldModelStepKindRecord.DescribeLocation => WorldModelStepKind.DescribeLocation,
            _ => WorldModelStepKind.Unspecified,
        },
        SceneId = step.SceneId?.ToString() ?? "",
        Title = step.Title,
        Status = step.Status switch
        {
            WorldModelStepStatusRecord.Running => WorldModelStepStatus.Running,
            WorldModelStepStatusRecord.Finished => WorldModelStepStatus.Finished,
            WorldModelStepStatusRecord.Failed => WorldModelStepStatus.Failed,
            WorldModelStepStatusRecord.Skipped => WorldModelStepStatus.Skipped,
            _ => WorldModelStepStatus.Unspecified,
        },
        StartedUnixMs = WorldModelMessages.Millis(step.StartedAt),
        FinishedUnixMs = WorldModelMessages.Millis(step.FinishedAt),
        ModelId = step.ModelId ?? "",
        InputTokens = step.InputTokens,
        OutputTokens = step.OutputTokens,
        CostUsdMicros = WorldModelMessages.Micros(step.Cost),
        Prompt = step.Prompt ?? "",
        Reply = step.Reply ?? "",
        Summary = step.Summary ?? "",
        Failure = step.Failure ?? "",
    };

    public static WorldModelBuildStatus ToStatus(WorldModelBuildStatusRecord status) => status switch
    {
        WorldModelBuildStatusRecord.Queued => WorldModelBuildStatus.Queued,
        WorldModelBuildStatusRecord.Running => WorldModelBuildStatus.Running,
        WorldModelBuildStatusRecord.Retrying => WorldModelBuildStatus.Retrying,
        WorldModelBuildStatusRecord.Finished => WorldModelBuildStatus.Finished,
        WorldModelBuildStatusRecord.Failed => WorldModelBuildStatus.Failed,
        WorldModelBuildStatusRecord.Cancelled => WorldModelBuildStatus.Cancelled,
        _ => WorldModelBuildStatus.Unspecified,
    };

    public static WorldModelBuildStatusRecord? ToStatus(WorldModelBuildStatus status) => status switch
    {
        WorldModelBuildStatus.Queued => WorldModelBuildStatusRecord.Queued,
        WorldModelBuildStatus.Running => WorldModelBuildStatusRecord.Running,
        WorldModelBuildStatus.Retrying => WorldModelBuildStatusRecord.Retrying,
        WorldModelBuildStatus.Finished => WorldModelBuildStatusRecord.Finished,
        WorldModelBuildStatus.Failed => WorldModelBuildStatusRecord.Failed,
        WorldModelBuildStatus.Cancelled => WorldModelBuildStatusRecord.Cancelled,
        _ => null,
    };
}
