using Grpc.Core;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Core;
using ProtoFast.Storage.Abstractions;
using SceneRecord = ProtoFast.Data.ThePlot.Entities.Scene;
using WorldModelBuildRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuild;

namespace ProtoFast.Api.Services.WorldModels;

/// <summary>
/// Starts, follows and stops a story's world model builds. The build itself runs in the
/// document-import worker, which the queue message reaches after the build row is committed.
/// </summary>
public sealed class WorldModelService(
    UserContext userContext,
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    ISceneRepository sceneRepository,
    IQueryFactory<SceneRecord, ISceneQuery> sceneQueries,
    IWorldModelBuildRepository buildRepository,
    IQueryFactory<WorldModelBuildRecord, IWorldModelBuildQuery> buildQueries,
    [FromKeyedServices(WorldModelQueues.BuildQueueKey)] IMessageQueue buildQueue,
    ILogger<WorldModelService> logger) : global::ProtoFast.Api.WorldModels.WorldModelsBase
{
    public const string BuildNotFound = "That build could not be found.";
    public const string BuildRunning = "A world model build is already running for this story.";
    public const string BuildEnded = "This build has already finished.";
    public const string NotQueued = "The build couldn’t be queued. Try again in a moment.";
    private const string Waiting = "Waiting for a worker";

    public override async Task<StartBuildReply> StartBuild(StartBuildRequest request, ServerCallContext context)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        var subject = userContext.CurrentUserId
                      ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Sign in to build a world model."));

        WorldModelBuildRecord build;
        using (var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(StartBuild)))
        {
            await storyScope.LockAsync(storyId, context.CancellationToken);
            if (await buildRepository.ExistsByQueryAsync(buildQueries.Create().InStory(storyId).Active(), context.CancellationToken))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, BuildRunning));
            }

            var scenes = await sceneRepository.GetByQueryAsync(sceneQueries.Create().InStory(storyId), s => s.Id, context.CancellationToken);
            build = new WorldModelBuildRecord
            {
                Id = Guid.NewGuid(),
                StoryId = storyId,
                Status = WorldModelBuildStatus.Queued,
                ScenesTotal = scenes.Count,
                Message = Waiting,
            };
            await buildRepository.AddAsync(build, context.CancellationToken);
            await unitOfWork.CommitAsync(context.CancellationToken);
        }

        if (!await QueueAsync(build, subject, context.CancellationToken))
        {
            await MarkNotQueuedAsync(build, context.CancellationToken);
        }

        return new StartBuildReply { Build = WorldModelMessages.ToMessage(build) };
    }

    public override async Task<GetLatestBuildReply> GetLatestBuild(GetLatestBuildRequest request, ServerCallContext context)
    {
        var storyId = StoryIds.Existing(request.StoryId, StoryErrors.StoryNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(GetLatestBuild));
        var build = await buildRepository.GetFirstByQueryAsync(
            buildQueries.Create().InStory(storyId).NewestFirst(), context.CancellationToken);
        if (build is null)
        {
            await storyScope.GetAsync(storyId, context.CancellationToken);
            return new GetLatestBuildReply();
        }

        return new GetLatestBuildReply { Build = WorldModelMessages.ToMessage(build) };
    }

    public override async Task<CancelBuildReply> CancelBuild(CancelBuildRequest request, ServerCallContext context)
    {
        var buildId = StoryIds.Existing(request.BuildId, BuildNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CancelBuild));
        var now = DateTime.UtcNow;
        var stopped = await buildRepository.UpdateByQueryAsync(
            buildQueries.Create().WithId(buildId).Active(),
            set => set
                .SetProperty(b => b.Status, WorldModelBuildStatus.Cancelled)
                .SetProperty(b => b.FinishedAt, now)
                .SetProperty(b => b.Message, "Stopped")
                .SetProperty(b => b.DateLastModified, now),
            context.CancellationToken);
        var build = await buildRepository.GetFirstByQueryAsync(buildQueries.Create().WithId(buildId), context.CancellationToken)
                    ?? throw StoryErrors.NotFound(BuildNotFound);
        if (stopped == 0)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, BuildEnded));
        }

        await unitOfWork.CommitAsync(context.CancellationToken);
        return new CancelBuildReply { Build = WorldModelMessages.ToMessage(build) };
    }

    /// <summary>Queued after the commit, so a message never names a build that was rolled back.</summary>
    private async Task<bool> QueueAsync(WorldModelBuildRecord build, string subject, CancellationToken cancellationToken)
    {
        try
        {
            await buildQueue.SendAsync(new WorldModelBuildRequested(build.Id, build.StoryId, subject), groupId: subject, cancellationToken);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Build {BuildId} was saved but could not be queued", build.Id);
            return false;
        }
    }

    // A build nothing will ever pick up is failed at once rather than left queued for good.
    private async Task MarkNotQueuedAsync(WorldModelBuildRecord build, CancellationToken cancellationToken)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(MarkNotQueuedAsync));
        var now = DateTime.UtcNow;
        await buildRepository.UpdateByQueryAsync(
            buildQueries.Create().WithId(build.Id).Active(),
            set => set
                .SetProperty(b => b.Status, WorldModelBuildStatus.Failed)
                .SetProperty(b => b.FinishedAt, now)
                .SetProperty(b => b.Message, NotQueued)
                .SetProperty(b => b.Failure, "The queue did not take the message.")
                .SetProperty(b => b.DateLastModified, now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        build.Status = WorldModelBuildStatus.Failed;
        build.FinishedAt = now;
        build.Message = NotQueued;
    }
}
