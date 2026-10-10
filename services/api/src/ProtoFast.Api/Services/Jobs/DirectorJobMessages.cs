using Google.Protobuf.WellKnownTypes;
using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Api.Services.Jobs;

public static class DirectorJobMessages
{
    public static Job ToMessage(DirectorJob job)
    {
        var message = new Job
        {
            Id = job.Id.ToString(),
            StoryId = job.StoryId.ToString(),
            Label = job.Label,
            Status = ToMessage(job.Status),
            StepsDone = job.StepsDone,
            Error = job.Error ?? "",
            Created = Timestamp.FromDateTime(DateTime.SpecifyKind(job.DateCreated, DateTimeKind.Utc)),
            Lane = job.Lane,
        };
        message.Steps.AddRange(JobSteps.Parser.ParseFrom(job.Steps).Steps);
        if (job.SceneId is { } sceneId)
        {
            message.SceneId = sceneId.ToString();
        }

        if (job.StartedAt is { } started)
        {
            message.Started = Timestamp.FromDateTime(DateTime.SpecifyKind(started, DateTimeKind.Utc));
        }

        if (job.FinishedAt is { } finished)
        {
            message.Finished = Timestamp.FromDateTime(DateTime.SpecifyKind(finished, DateTimeKind.Utc));
        }

        return message;
    }

    private static JobStatus ToMessage(DirectorJobStatus status) => status switch
    {
        DirectorJobStatus.Queued => JobStatus.Queued,
        DirectorJobStatus.Running => JobStatus.Running,
        DirectorJobStatus.Succeeded => JobStatus.Succeeded,
        DirectorJobStatus.Failed => JobStatus.Failed,
        DirectorJobStatus.Cancelled => JobStatus.Cancelled,
        _ => JobStatus.Unspecified,
    };
}
