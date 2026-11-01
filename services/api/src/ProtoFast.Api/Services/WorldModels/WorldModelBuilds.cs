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
/// Starts and stops a story's world model builds as the story's writer, whose rows they are. The
/// build itself runs in the document-import worker, which the queue message reaches after the
/// build row is committed.
/// </summary>
public sealed class WorldModelBuilds(
    UserContext userContext,
    IUnitOfWorkFactory unitOfWorkFactory,
    StoryScope storyScope,
    ISceneRepository sceneRepository,
    IQueryFactory<SceneRecord, ISceneQuery> sceneQueries,
    IWorldModelBuildRepository buildRepository,
    IQueryFactory<WorldModelBuildRecord, IWorldModelBuildQuery> buildQueries,
    [FromKeyedServices(WorldModelQueues.BuildQueueKey)] IMessageQueue buildQueue,
    ILogger<WorldModelBuilds> logger)
{
    public const string BuildNotFound = "Build not found.";
    public const string BuildRunning = "A world model build is already running for this story.";
    public const string BuildEnded = "This build has already finished.";
    public const string NotQueued = "The build couldn’t be queued. Try again in a moment.";
    private const string Waiting = "Waiting for a worker";

    /// <summary>NotFound unless the writer owns the story; FailedPrecondition while a build is active.</summary>
    public async Task<Guid> StartAsync(string writerId, Guid storyId, CancellationToken cancellationToken)
    {
        using var writer = userContext.SetCurrentUser(writerId);

        WorldModelBuildRecord build;
        using (var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(StartAsync)))
        {
            await storyScope.LockAsync(storyId, cancellationToken);
            if (await buildRepository.ExistsByQueryAsync(buildQueries.Create().InStory(storyId).Active(), cancellationToken))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, BuildRunning));
            }

            var scenes = await sceneRepository.GetByQueryAsync(sceneQueries.Create().InStory(storyId), s => s.Id, cancellationToken);
            build = new WorldModelBuildRecord
            {
                Id = Guid.NewGuid(),
                StoryId = storyId,
                Status = WorldModelBuildStatus.Queued,
                ScenesTotal = scenes.Count,
                Message = Waiting,
            };
            await buildRepository.AddAsync(build, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        if (!await QueueAsync(build, writerId, cancellationToken))
        {
            await MarkNotQueuedAsync(build.Id, cancellationToken);
        }

        return build.Id;
    }

    /// <summary>NotFound unless the writer owns the build; FailedPrecondition once it has ended.</summary>
    public async Task CancelAsync(string writerId, Guid buildId, CancellationToken cancellationToken)
    {
        using var writer = userContext.SetCurrentUser(writerId);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CancelAsync));
        var now = DateTime.UtcNow;
        var stopped = await buildRepository.UpdateByQueryAsync(
            buildQueries.Create().WithId(buildId).Active(),
            set => set
                .SetProperty(b => b.Status, WorldModelBuildStatus.Cancelled)
                .SetProperty(b => b.FinishedAt, now)
                .SetProperty(b => b.Message, "Stopped")
                .SetProperty(b => b.DateLastModified, now),
            cancellationToken);
        if (stopped == 0)
        {
            throw await buildRepository.ExistsByQueryAsync(buildQueries.Create().WithId(buildId), cancellationToken)
                ? new RpcException(new Status(StatusCode.FailedPrecondition, BuildEnded))
                : StoryErrors.NotFound(BuildNotFound);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }

    /// <summary>Queued after the commit, so a message never names a build that was rolled back.</summary>
    private async Task<bool> QueueAsync(WorldModelBuildRecord build, string writerId, CancellationToken cancellationToken)
    {
        try
        {
            await buildQueue.SendAsync(new WorldModelBuildRequested(build.Id, build.StoryId, writerId), groupId: writerId, cancellationToken);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Build {BuildId} was saved but could not be queued", build.Id);
            return false;
        }
    }

    // A build nothing will ever pick up is failed at once rather than left queued for good.
    private async Task MarkNotQueuedAsync(Guid buildId, CancellationToken cancellationToken)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(MarkNotQueuedAsync));
        var now = DateTime.UtcNow;
        await buildRepository.UpdateByQueryAsync(
            buildQueries.Create().WithId(buildId).Active(),
            set => set
                .SetProperty(b => b.Status, WorldModelBuildStatus.Failed)
                .SetProperty(b => b.FinishedAt, now)
                .SetProperty(b => b.Message, NotQueued)
                .SetProperty(b => b.Failure, "The queue did not take the message.")
                .SetProperty(b => b.DateLastModified, now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
