using Grpc.Core;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Api.Services.WorldModels;
using ProtoFast.DocumentImport.Core;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Starting and stopping a writer's build from theplot's console.</summary>
public sealed class WorldModelBuildTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);
    private readonly Operator _theplot = new(database, "operators", "admin-theplot");

    [Fact]
    public async Task Starting_a_build_queues_it_as_the_writers_with_the_storys_scene_count()
    {
        var story = await _writer.CreateStoryAsync();
        await _writer.CreateSceneAsync(story.Containers[0].Id, 1, "Second", Heading(), Action("A beat."));

        var build = await StartAsync(story.Id);

        Assert.Equal((WorldModelBuildStatus.Queued, 2, 0, story.Id, _writer.UserId),
            (build.Status, build.ScenesTotal, build.ScenesDone, build.StoryId, build.OwnerId));
        Assert.NotEqual(0, build.RequestedUnixMs);
        var queued = Assert.Single(database.WorldModelQueue.Sent.OfType<WorldModelBuildRequested>(), m => m.BuildId.ToString() == build.Id);
        Assert.Equal((Guid.Parse(story.Id), _writer.UserId), (queued.StoryId, queued.UserId));
    }

    [Fact]
    public async Task Only_one_build_runs_per_story_at_a_time()
    {
        var story = await _writer.CreateStoryAsync();
        await StartAsync(story.Id);

        var error = await Assert.ThrowsAsync<RpcException>(() => StartAsync(story.Id));

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        Assert.Equal(WorldModelBuilds.BuildRunning, error.Status.Detail);
    }

    [Fact]
    public async Task A_missing_story_or_build_answers_as_not_found()
    {
        var start = await Assert.ThrowsAsync<RpcException>(() => StartAsync(Guid.NewGuid().ToString()));
        var cancel = await Assert.ThrowsAsync<RpcException>(() => CancelAsync(Guid.NewGuid().ToString()));

        Assert.Equal(StatusCode.NotFound, start.StatusCode);
        Assert.Equal(StatusCode.NotFound, cancel.StatusCode);
    }

    [Fact]
    public async Task Cancelling_stops_a_queued_build_once()
    {
        var story = await _writer.CreateStoryAsync();
        var started = await StartAsync(story.Id);

        var cancelled = await CancelAsync(started.Id);
        var again = await Assert.ThrowsAsync<RpcException>(() => CancelAsync(started.Id));

        Assert.Equal(WorldModelBuildStatus.Cancelled, cancelled.Status);
        Assert.NotEqual(0, cancelled.FinishedUnixMs);
        Assert.Equal(StatusCode.FailedPrecondition, again.StatusCode);

        // Cancelled, the story can be built again.
        var next = await StartAsync(story.Id);
        Assert.NotEqual(started.Id, next.Id);
    }

    [Fact]
    public async Task Listing_by_story_answers_only_that_storys_builds_newest_first()
    {
        var story = await _writer.CreateStoryAsync();
        var other = await _writer.CreateStoryAsync("Another");
        var first = await StartAsync(story.Id);
        await CancelAsync(first.Id);
        var second = await StartAsync(story.Id);
        await StartAsync(other.Id);

        var listed = await _theplot.Call<TheplotWorldModelsService, ListWorldModelBuildsReply>(
            (s, c) => s.ListBuilds(new ListWorldModelBuildsRequest { StoryId = story.Id }, c));

        Assert.Equal([second.Id, first.Id], listed.Builds.Select(b => b.Id));
        Assert.Equal(2, listed.Total);
    }

    [Fact]
    public async Task A_build_the_queue_refuses_fails_at_once()
    {
        var story = await _writer.CreateStoryAsync();
        database.WorldModelQueue.Refuse = true;
        try
        {
            var build = await StartAsync(story.Id);

            Assert.Equal((WorldModelBuildStatus.Failed, WorldModelBuilds.NotQueued), (build.Status, build.Message));
        }
        finally
        {
            database.WorldModelQueue.Refuse = false;
        }
    }

    private async Task<WorldModelBuildHeader> StartAsync(string storyId) =>
        (await _theplot.Call<TheplotWorldModelsService, StartWorldModelBuildReply>(
            (s, c) => s.StartBuild(new StartWorldModelBuildRequest { StoryId = storyId }, c))).Build;

    private async Task<WorldModelBuildHeader> CancelAsync(string buildId) =>
        (await _theplot.Call<TheplotWorldModelsService, CancelWorldModelBuildReply>(
            (s, c) => s.CancelBuild(new CancelWorldModelBuildRequest { BuildId = buildId }, c))).Build;
}
