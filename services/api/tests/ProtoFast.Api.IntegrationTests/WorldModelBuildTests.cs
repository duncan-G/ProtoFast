using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Services.WorldModels;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Core;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;
using WorldModelBuildRecord = ProtoFast.Data.ThePlot.Entities.WorldModelBuild;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Starting, following and stopping a build from the writer's side.</summary>
public sealed class WorldModelBuildTests(StoryDatabase database)
{
    private readonly Writer _owner = new(database);
    private readonly Writer _stranger = new(database);

    [Fact]
    public async Task Starting_a_build_queues_it_with_the_storys_scene_count_and_tells_the_worker()
    {
        var story = await _owner.CreateStoryAsync();
        await _owner.CreateSceneAsync(story.Containers[0].Id, 1, "Second", Heading(), Action("A beat."));

        var reply = await _owner.Call<WorldModelService, StartBuildReply>(
            (s, c) => s.StartBuild(new StartBuildRequest { StoryId = story.Id }, c));

        var build = reply.Build;
        Assert.Equal((WorldModelBuildState.Queued, 2, 0, story.Id), (build.State, build.ScenesTotal, build.ScenesDone, build.StoryId));
        Assert.NotEqual(0, build.RequestedUnixMs);
        var queued = Assert.Single(database.WorldModelQueue.Sent.OfType<WorldModelBuildRequested>(), m => m.BuildId.ToString() == build.Id);
        Assert.Equal((Guid.Parse(story.Id), _owner.UserId), (queued.StoryId, queued.UserId));

        var latest = await _owner.Call<WorldModelService, GetLatestBuildReply>(
            (s, c) => s.GetLatestBuild(new GetLatestBuildRequest { StoryId = story.Id }, c));
        Assert.Equal(build.Id, latest.Build?.Id);
    }

    [Fact]
    public async Task Only_one_build_runs_per_story_at_a_time()
    {
        var story = await _owner.CreateStoryAsync();
        await _owner.Call<WorldModelService, StartBuildReply>((s, c) => s.StartBuild(new StartBuildRequest { StoryId = story.Id }, c));

        var error = await Assert.ThrowsAsync<RpcException>(() => _owner.Call<WorldModelService, StartBuildReply>(
            (s, c) => s.StartBuild(new StartBuildRequest { StoryId = story.Id }, c)));

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        Assert.Equal(WorldModelService.BuildRunning, error.Status.Detail);
    }

    [Fact]
    public async Task A_story_with_no_build_answers_with_none_and_a_strangers_story_as_not_found()
    {
        var story = await _owner.CreateStoryAsync();

        var none = await _owner.Call<WorldModelService, GetLatestBuildReply>(
            (s, c) => s.GetLatestBuild(new GetLatestBuildRequest { StoryId = story.Id }, c));
        var start = await Assert.ThrowsAsync<RpcException>(() => _stranger.Call<WorldModelService, StartBuildReply>(
            (s, c) => s.StartBuild(new StartBuildRequest { StoryId = story.Id }, c)));
        var latest = await Assert.ThrowsAsync<RpcException>(() => _stranger.Call<WorldModelService, GetLatestBuildReply>(
            (s, c) => s.GetLatestBuild(new GetLatestBuildRequest { StoryId = story.Id }, c)));

        Assert.Null(none.Build);
        Assert.Equal(StatusCode.NotFound, start.StatusCode);
        Assert.Equal(StatusCode.NotFound, latest.StatusCode);
    }

    [Fact]
    public async Task Cancelling_stops_a_queued_build_once_and_a_stranger_cannot()
    {
        var story = await _owner.CreateStoryAsync();
        var started = await _owner.Call<WorldModelService, StartBuildReply>((s, c) => s.StartBuild(new StartBuildRequest { StoryId = story.Id }, c));

        var strangers = await Assert.ThrowsAsync<RpcException>(() => _stranger.Call<WorldModelService, CancelBuildReply>(
            (s, c) => s.CancelBuild(new CancelBuildRequest { BuildId = started.Build.Id }, c)));
        var cancelled = await _owner.Call<WorldModelService, CancelBuildReply>(
            (s, c) => s.CancelBuild(new CancelBuildRequest { BuildId = started.Build.Id }, c));
        var again = await Assert.ThrowsAsync<RpcException>(() => _owner.Call<WorldModelService, CancelBuildReply>(
            (s, c) => s.CancelBuild(new CancelBuildRequest { BuildId = started.Build.Id }, c)));

        Assert.Equal(StatusCode.NotFound, strangers.StatusCode);
        Assert.Equal(WorldModelBuildState.Cancelled, cancelled.Build.State);
        Assert.NotEqual(0, cancelled.Build.FinishedUnixMs);
        Assert.Equal(StatusCode.FailedPrecondition, again.StatusCode);

        // Cancelled, the story can be built again.
        var next = await _owner.Call<WorldModelService, StartBuildReply>((s, c) => s.StartBuild(new StartBuildRequest { StoryId = story.Id }, c));
        Assert.NotEqual(started.Build.Id, next.Build.Id);
    }

    [Fact]
    public async Task A_build_the_queue_refuses_fails_at_once()
    {
        var story = await _owner.CreateStoryAsync();
        database.WorldModelQueue.Refuse = true;
        try
        {
            var reply = await _owner.Call<WorldModelService, StartBuildReply>((s, c) => s.StartBuild(new StartBuildRequest { StoryId = story.Id }, c));

            Assert.Equal((WorldModelBuildState.Failed, WorldModelService.NotQueued), (reply.Build.State, reply.Build.Message));
        }
        finally
        {
            database.WorldModelQueue.Refuse = false;
        }
    }

    [Fact]
    public async Task Progress_the_worker_records_reaches_the_writer_with_its_cost()
    {
        var story = await _owner.CreateStoryAsync();
        var started = await _owner.Call<WorldModelService, StartBuildReply>((s, c) => s.StartBuild(new StartBuildRequest { StoryId = story.Id }, c));
        await using (var scope = database.Services.CreateAsyncScope())
        {
            using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(_owner.UserId);
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("worker");
            var builds = scope.ServiceProvider.GetRequiredService<IWorldModelBuildRepository>();
            var queries = scope.ServiceProvider.GetRequiredService<IQueryFactory<WorldModelBuildRecord, IWorldModelBuildQuery>>();
            await builds.UpdateByQueryAsync(
                queries.Create().WithId(Guid.Parse(started.Build.Id)),
                set => set
                    .SetProperty(b => b.Status, WorldModelBuildStatus.Running)
                    .SetProperty(b => b.ScenesDone, 1)
                    .SetProperty(b => b.Cost, 0.4125m)
                    .SetProperty(b => b.Message, "Reading Scene 2"),
                default);
            await unitOfWork.CommitAsync(default);
        }

        var latest = await _owner.Call<WorldModelService, GetLatestBuildReply>(
            (s, c) => s.GetLatestBuild(new GetLatestBuildRequest { StoryId = story.Id }, c));

        Assert.Equal((WorldModelBuildState.Running, 1, 412_500L, "Reading Scene 2"),
            (latest.Build!.State, latest.Build.ScenesDone, latest.Build.CostUsdMicros, latest.Build.Message));
    }
}
