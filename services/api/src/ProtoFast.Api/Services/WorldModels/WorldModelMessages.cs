using ProtoFast.Data.ThePlot.Entities;
using WorldModelBuildRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuild;

namespace ProtoFast.Api.Services.WorldModels;

public static class WorldModelMessages
{
    public static WorldModelBuild ToMessage(WorldModelBuildRecord build) => new()
    {
        Id = build.Id.ToString(),
        StoryId = build.StoryId.ToString(),
        State = ToState(build.Status),
        ScenesTotal = build.ScenesTotal,
        ScenesDone = build.ScenesDone,
        Message = build.Message ?? "",
        CostUsdMicros = Micros(build.Cost),
        RequestedUnixMs = Millis(build.DateCreated),
        StartedUnixMs = Millis(build.StartedAt),
        FinishedUnixMs = Millis(build.FinishedAt),
        PartCount = build.PartCount,
        ObjectCount = build.ObjectCount,
        ObservationCount = build.ObservationCount,
    };

    public static WorldModelBuildState ToState(WorldModelBuildStatus status) => status switch
    {
        WorldModelBuildStatus.Queued => WorldModelBuildState.Queued,
        WorldModelBuildStatus.Running => WorldModelBuildState.Running,
        WorldModelBuildStatus.Retrying => WorldModelBuildState.Retrying,
        WorldModelBuildStatus.Finished => WorldModelBuildState.Finished,
        WorldModelBuildStatus.Failed => WorldModelBuildState.Failed,
        WorldModelBuildStatus.Cancelled => WorldModelBuildState.Cancelled,
        _ => WorldModelBuildState.Unspecified,
    };

    public static long Micros(decimal usd) => (long)Math.Round(usd * 1_000_000m);

    /// <summary>Stored as UTC without a kind, as the date-stamp interceptor writes them.</summary>
    public static long Millis(DateTime? at) =>
        at is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds() : 0;
}
