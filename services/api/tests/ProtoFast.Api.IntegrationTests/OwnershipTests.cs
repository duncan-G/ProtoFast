using Grpc.Core;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class OwnershipTests(StoryDatabase database)
{
    private readonly Writer _owner = new(database);
    private readonly Writer _stranger = new(database);

    [Fact]
    public async Task Another_users_story_reads_and_writes_as_not_found()
    {
        var story = await _owner.CreateStoryAsync();
        var mara = await _owner.CreateCharacterAsync(story.Id, "Mara");
        var scene = await _owner.GetSceneAsync(story.Containers[0].Scenes[0].Id);
        var container = story.Containers[0];

        Task[] attempts =
        [
            _stranger.GetStoryAsync(story.Id),
            _stranger.GetSceneAsync(scene.Id),
            _stranger.SaveSceneAsync(scene),
            _stranger.CreateSceneAsync(container.Id, 0, "Mine now", Heading()),
            _stranger.CreateCharacterAsync(story.Id, "Bolt"),
            _stranger.Call((s, c) => s.UpdateCharacter(
                new UpdateCharacterRequest { CharacterId = mara.Id, Name = "Stolen", Kind = "Human", Hue = 1 }, c)),
            _stranger.Call((s, c) => s.DeleteCharacter(new DeleteCharacterRequest { CharacterId = mara.Id }, c)),
            _stranger.Call((s, c) => s.RenameContainer(new RenameContainerRequest { ContainerId = container.Id, Label = "Mine" }, c)),
            _stranger.Call((s, c) => s.SaveVocabulary(new SaveVocabularyRequest { StoryId = story.Id, Vocabulary = new() }, c)),
            _stranger.Call((s, c) => s.UpdateStory(new UpdateStoryRequest { StoryId = story.Id, Title = "Mine" }, c)),
            _stranger.Call((s, c) => s.DeleteStory(new DeleteStoryRequest { StoryId = story.Id }, c)),
        ];

        foreach (var attempt in attempts)
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => attempt);
            Assert.Equal(StatusCode.NotFound, error.StatusCode);
        }

        Assert.Empty((await _stranger.Call((s, c) => s.ListStories(new ListStoriesRequest(), c))).Stories);
        var intact = await _owner.GetStoryAsync(story.Id);
        Assert.Equal("Mara", Assert.Single(intact.Characters).Name);
        Assert.Equal("Act I", intact.Containers[0].Label);
    }

    [Fact]
    public async Task A_scene_cannot_point_at_another_users_character()
    {
        var theirs = await _stranger.CreateStoryAsync();
        var bolt = await _stranger.CreateCharacterAsync(theirs.Id, "Bolt");
        var story = await _owner.CreateStoryAsync();

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            _owner.WriteFirstSceneAsync(story, Heading(), Dialogue(bolt.Id, "Hello.")));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }
}
