using System.Diagnostics;
using Google.Protobuf;
using Grpc.Core;
using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Api.Services.Jobs;

/// <summary>
/// The author's side of the director job queue. Steps are checked for shape and for the story and
/// scene being the caller's; what each one names is checked when it runs, as the call would be.
/// </summary>
public sealed class DirectorJobsService(
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    DirectionScope directionScope,
    IDirectorJobRepository jobRepository,
    IQueryFactory<DirectorJob, IDirectorJobQuery> jobQueries,
    DirectorJobSignal signal,
    DirectorJobOptions options) : DirectorJobs.DirectorJobsBase
{
    private const int LabelLength = 255;

    public override async Task<StartJobReply> StartJob(StartJobRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        if (request.Steps.Count == 0)
        {
            throw StoryErrors.Invalid(DirectorJobErrors.NoSteps);
        }

        if (request.Steps.Count > options.MaxSteps)
        {
            throw StoryErrors.Invalid(DirectorJobErrors.TooManySteps);
        }

        if (request.Steps.Any(s => s.WorkCase == JobStep.WorkOneofCase.None))
        {
            throw StoryErrors.Invalid(DirectorJobErrors.EmptyStep);
        }

        var scenes = request.Steps.Select(SceneOf).OfType<string>().Distinct().ToList();
        if (scenes.Count > 1)
        {
            throw StoryErrors.Invalid(DirectorJobErrors.ScenesDiffer);
        }

        var sceneId = scenes.Count == 1 ? StoryIds.Existing(scenes[0], StoryErrors.SceneNotFound) : (Guid?)null;
        var label = request.Label.Trim();
        var steps = new JobSteps { Steps = { request.Steps } };
        var job = new DirectorJob
        {
            Id = Guid.NewGuid(),
            StoryId = storyId,
            SceneId = sceneId,
            Lane = sceneId is { } lane ? $"scene:{lane}" : $"library:{storyId}",
            Label = label.Length == 0 ? "Director work" : label[..Math.Min(label.Length, LabelLength)],
            Steps = steps.ToByteArray(),
            StepCount = steps.Steps.Count,
            FollowVariations = request.FollowVariations,
            Status = DirectorJobStatus.Queued,
            TraceParent = Activity.Current?.Id,
        };

        using (var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(StartJob)))
        {
            await storyScope.GetAsync(storyId, cancellationToken);
            if (sceneId is { } scene && await directionScope.StoryIdAsync(scene, cancellationToken) != storyId)
            {
                throw StoryErrors.NotFound(StoryErrors.SceneNotFound);
            }

            await jobRepository.AddAsync(job, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        signal.Wake();
        return new StartJobReply { Job = DirectorJobMessages.ToMessage(job) };
    }

    public override async Task<ListJobsReply> ListJobs(ListJobsRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListJobs));
        var jobs = await jobRepository.GetByQueryAsync(jobQueries.Create().InOrder(), j => new DirectorJob
        {
            Id = j.Id,
            StoryId = j.StoryId,
            SceneId = j.SceneId,
            Lane = j.Lane,
            Label = j.Label,
            Steps = j.Steps,
            StepCount = j.StepCount,
            StepsDone = j.StepsDone,
            Status = j.Status,
            Error = j.Error,
            StartedAt = j.StartedAt,
            FinishedAt = j.FinishedAt,
            DateCreated = j.DateCreated,
        }, context.CancellationToken);
        var reply = new ListJobsReply();
        reply.Jobs.AddRange(jobs.Select(DirectorJobMessages.ToMessage));
        return reply;
    }

    public override async Task<GetJobReply> GetJob(GetJobRequest request, ServerCallContext context)
    {
        var id = StoryIds.Existing(request.JobId, DirectorJobErrors.JobNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(GetJob));
        var job = await jobRepository.GetFirstByQueryAsync(jobQueries.Create().WithId(id), context.CancellationToken)
                  ?? throw StoryErrors.NotFound(DirectorJobErrors.JobNotFound);
        var reply = new GetJobReply { Job = DirectorJobMessages.ToMessage(job) };
        if (job.Results is { } results)
        {
            reply.Results.AddRange(JobResults.Parser.ParseFrom(results).Results.Skip(Math.Max(request.FromStep, 0)));
        }

        return reply;
    }

    public override async Task<CancelJobReply> CancelJob(CancelJobRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var id = StoryIds.Existing(request.JobId, DirectorJobErrors.JobNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CancelJob));
        await jobRepository.UpdateByQueryAsync(
            jobQueries.Create().WithId(id).WithStatus(DirectorJobStatus.Queued, DirectorJobStatus.Running),
            s => s
                .SetProperty(j => j.Status, DirectorJobStatus.Cancelled)
                .SetProperty(j => j.FinishedAt, DateTime.UtcNow)
                .SetProperty(j => j.LeaseUntil, (DateTime?)null)
                .SetProperty(j => j.DateLastModified, DateTime.UtcNow),
            cancellationToken);
        var job = await jobRepository.GetFirstByQueryAsync(jobQueries.Create().WithId(id), cancellationToken)
                  ?? throw StoryErrors.NotFound(DirectorJobErrors.JobNotFound);
        await unitOfWork.CommitAsync(cancellationToken);
        return new CancelJobReply { Job = DirectorJobMessages.ToMessage(job) };
    }

    public override async Task<DismissJobsReply> DismissJobs(DismissJobsRequest request, ServerCallContext context)
    {
        var ids = request.JobIds.Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty).ToList();
        if (ids.Count == 0)
        {
            return new DismissJobsReply();
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DismissJobs));
        await jobRepository.DeleteByQueryAsync(
            jobQueries.Create().WithIds(ids)
                .WithStatus(DirectorJobStatus.Succeeded, DirectorJobStatus.Failed, DirectorJobStatus.Cancelled),
            context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DismissJobsReply();
    }

    private static string? SceneOf(JobStep step) => step.WorkCase switch
    {
        JobStep.WorkOneofCase.Direct => step.Direct.SceneId,
        JobStep.WorkOneofCase.Audio => step.Audio.SceneId,
        JobStep.WorkOneofCase.FrameImage => step.FrameImage.SceneId,
        JobStep.WorkOneofCase.LookImage => step.LookImage.SceneId,
        _ => null,
    };
}
