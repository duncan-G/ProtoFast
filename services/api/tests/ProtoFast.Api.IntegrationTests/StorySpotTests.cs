using Grpc.Core;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class StorySpotTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    [Fact]
    public async Task A_story_opens_where_the_writer_left_it()
    {
        var story = await _writer.CreateStoryAsync();
        var line = Action("Mara climbs.");
        var later = await _writer.CreateSceneAsync(story.Containers[0].Id, 1, "Later", Heading(), line);

        Assert.Null((await _writer.GetStoryAsync(story.Id)).Spot);

        await _writer.SaveSpotAsync(story.Id, later.Id, line.Id);
        var spot = (await _writer.GetStoryAsync(story.Id)).Spot;
        Assert.Equal(later.Id, spot.SceneId);
        Assert.Equal(line.Id, spot.ElementId);

        await _writer.SaveSpotAsync(story.Id, later.Id);
        Assert.False((await _writer.GetStoryAsync(story.Id)).Spot.HasElementId);
    }

    [Fact]
    public async Task Saving_the_spot_leaves_the_story_unmodified()
    {
        var story = await _writer.CreateStoryAsync();
        var before = await ModifiedAsync(story.Id);
        await Task.Delay(TimeSpan.FromSeconds(1.1), TestContext.Current.CancellationToken);

        await _writer.SaveSpotAsync(story.Id, story.Containers[0].Scenes[0].Id);

        Assert.Equal(before, await ModifiedAsync(story.Id));
    }

    [Fact]
    public async Task A_spot_in_a_deleted_scene_is_forgotten()
    {
        var story = await _writer.CreateStoryAsync();
        var later = await _writer.CreateSceneAsync(story.Containers[0].Id, 1, "Later", Heading());
        await _writer.SaveSpotAsync(story.Id, later.Id);

        await _writer.Call((s, c) => s.DeleteScene(new DeleteSceneRequest { SceneId = later.Id }, c));

        Assert.Null((await _writer.GetStoryAsync(story.Id)).Spot);
    }

    [Fact]
    public async Task A_spot_must_be_a_scene_of_the_story()
    {
        var story = await _writer.CreateStoryAsync();
        var other = await _writer.CreateStoryAsync("Another");

        var otherScene = await Assert.ThrowsAsync<RpcException>(
            () => _writer.SaveSpotAsync(story.Id, other.Containers[0].Scenes[0].Id));
        var badBlock = await Assert.ThrowsAsync<RpcException>(
            () => _writer.SaveSpotAsync(story.Id, story.Containers[0].Scenes[0].Id, "row-1"));

        Assert.Equal(StatusCode.NotFound, otherScene.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, badBlock.StatusCode);
        Assert.Null((await _writer.GetStoryAsync(story.Id)).Spot);
    }

    private async Task<long> ModifiedAsync(string storyId)
    {
        var stories = (await _writer.Call((s, c) => s.ListStories(new ListStoriesRequest(), c))).Stories;
        return stories.Single(s => s.Id == storyId).LastModifiedUnixSeconds;
    }
}
