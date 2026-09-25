using Grpc.Core;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class StoryTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    [Fact]
    public async Task A_new_story_opens_on_one_untitled_scene_with_an_empty_heading()
    {
        var created = await _writer.CreateStoryAsync("  The Signal  ");

        var story = await _writer.GetStoryAsync(created.Id);
        Assert.Equal("The Signal", story.Title);
        var act = Assert.Single(story.Containers);
        Assert.Equal("Act I", act.Label);
        var summary = Assert.Single(act.Scenes);
        Assert.Equal("Untitled scene", summary.Title);
        Assert.Equal(created, story);

        var heading = Assert.Single((await _writer.GetSceneAsync(summary.Id)).Elements);
        Assert.Equal(SceneElementType.Heading, heading.Type);
        Assert.False(heading.HasLocationId);
        Assert.False(heading.HasTimeOfDay);
    }

    [Fact]
    public async Task Stories_list_most_recently_modified_first()
    {
        var older = await _writer.CreateStoryAsync("Older");
        var newer = await _writer.CreateStoryAsync("Newer");
        await Task.Delay(TimeSpan.FromSeconds(1.1), TestContext.Current.CancellationToken);
        await _writer.CreateCharacterAsync(older.Id, "Mara");

        var stories = (await _writer.Call((s, c) => s.ListStories(new ListStoriesRequest(), c))).Stories;

        Assert.Equal([older.Id, newer.Id], stories.Select(s => s.Id));
        Assert.True(stories[0].LastModifiedUnixSeconds > stories[0].CreatedUnixSeconds);
    }

    [Fact]
    public async Task Titles_and_labels_are_required_and_bounded()
    {
        var story = await _writer.CreateStoryAsync();

        var blank = await Assert.ThrowsAsync<RpcException>(() => _writer.CreateStoryAsync(" "));
        var longTitle = await Assert.ThrowsAsync<RpcException>(() => _writer.Call((s, c) => s.UpdateStory(
            new UpdateStoryRequest { StoryId = story.Id, Title = new string('x', 256) }, c)));
        var label = await Assert.ThrowsAsync<RpcException>(() => _writer.Call((s, c) => s.CreateContainer(
            new CreateContainerRequest { StoryId = story.Id, Label = "" }, c)));

        Assert.Equal("A story needs a title.", blank.Status.Detail);
        Assert.Equal(StatusCode.InvalidArgument, longTitle.StatusCode);
        Assert.Equal("A container needs a label.", label.Status.Detail);
    }

    [Fact]
    public async Task Containers_append_and_rename()
    {
        var story = await _writer.CreateStoryAsync();
        var act = (await _writer.Call((s, c) => s.CreateContainer(
            new CreateContainerRequest { StoryId = story.Id, Label = " Act II " }, c))).Container;
        await _writer.Call((s, c) => s.RenameContainer(
            new RenameContainerRequest { ContainerId = act.Id, Label = "Part Two" }, c));
        await _writer.Call((s, c) => s.UpdateStory(new UpdateStoryRequest { StoryId = story.Id, Title = "Renamed" }, c));

        var outline = await _writer.GetStoryAsync(story.Id);
        Assert.Equal("Renamed", outline.Title);
        Assert.Equal(["Act I", "Part Two"], outline.Containers.Select(c => c.Label));
        Assert.Equal([0, 1], outline.Containers.Select(c => c.Position));
    }

    [Fact]
    public async Task Deleting_a_story_takes_everything_in_it()
    {
        var story = await _writer.CreateStoryAsync();
        await _writer.CreateCharacterAsync(story.Id, "Mara");
        var sceneId = story.Containers[0].Scenes[0].Id;

        await _writer.Call((s, c) => s.DeleteStory(new DeleteStoryRequest { StoryId = story.Id }, c));

        Assert.Equal(StatusCode.NotFound, (await Assert.ThrowsAsync<RpcException>(() => _writer.GetStoryAsync(story.Id))).StatusCode);
        Assert.Equal(StatusCode.NotFound, (await Assert.ThrowsAsync<RpcException>(() => _writer.GetSceneAsync(sceneId))).StatusCode);
    }
}
