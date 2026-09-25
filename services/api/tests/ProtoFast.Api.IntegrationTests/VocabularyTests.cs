using Grpc.Core;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class VocabularyTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    [Theory]
    [InlineData("night", null, null, "“NIGHT” is already on the list.")]
    [InlineData(null, " cut to ", null, "“CUT TO” is already on the list.")]
    [InlineData(null, null, "robot", "“Robot” is already on the list.")]
    [InlineData("golden hour", null, null, "“GOLDEN HOUR” is already on the list.")]
    public async Task Labels_already_on_the_list_are_refused_in_any_case(
        string? timeOfDay, string? transition, string? kind, string message)
    {
        var story = await _writer.CreateStoryAsync();
        var vocabulary = new StoryVocabulary();
        if (timeOfDay is not null)
        {
            vocabulary.TimesOfDay.AddRange(["GOLDEN HOUR", timeOfDay]);
        }

        if (transition is not null)
        {
            vocabulary.Transitions.Add(transition);
        }

        if (kind is not null)
        {
            vocabulary.CharacterKinds.Add(new CharacterKind { Label = kind, AvatarShape = AvatarShape.Star });
        }

        var error = await Assert.ThrowsAsync<RpcException>(() => SaveAsync(story.Id, vocabulary));

        Assert.Equal(StatusCode.AlreadyExists, error.StatusCode);
        Assert.Equal(message, error.Status.Detail);
    }

    [Fact]
    public async Task Blank_and_overlong_labels_are_refused()
    {
        var story = await _writer.CreateStoryAsync();

        var blank = await Assert.ThrowsAsync<RpcException>(() => SaveAsync(story.Id, new StoryVocabulary { TimesOfDay = { "  " } }));
        var longLabel = await Assert.ThrowsAsync<RpcException>(() => SaveAsync(
            story.Id, new StoryVocabulary { Transitions = { new string('X', 65) } }));

        Assert.Equal("Type a label first.", blank.Status.Detail);
        Assert.Equal("Labels are limited to 64 characters.", longLabel.Status.Detail);
    }

    [Fact]
    public async Task Additions_are_stored_uppercased_and_become_usable_in_scenes()
    {
        var story = await _writer.CreateStoryAsync();
        await SaveAsync(story.Id, new StoryVocabulary
        {
            TimesOfDay = { " golden hour " },
            Transitions = { "wipe to" },
            CharacterKinds = { new CharacterKind { Label = " Ghost ", AvatarShape = AvatarShape.Shield } },
        });

        var saved = (await _writer.GetStoryAsync(story.Id)).Vocabulary;
        Assert.Equal(["GOLDEN HOUR"], saved.TimesOfDay);
        Assert.Equal(["WIPE TO"], saved.Transitions);
        Assert.Equal(new CharacterKind { Label = "Ghost", AvatarShape = AvatarShape.Shield }, Assert.Single(saved.CharacterKinds));

        var scene = await _writer.WriteFirstSceneAsync(story, Heading(null, "GOLDEN HOUR"), Transition("WIPE TO"));
        Assert.Equal("GOLDEN HOUR", scene.Elements[0].TimeOfDay);
        Assert.Equal("GOLDEN HOUR", (await _writer.GetStoryAsync(story.Id)).Containers[0].Scenes[0].OpeningTimeOfDay);
        Assert.Equal("Ghost", (await _writer.CreateCharacterAsync(story.Id, "Wisp", "ghost")).Kind);
    }

    private Task<SaveVocabularyReply> SaveAsync(string storyId, StoryVocabulary vocabulary) =>
        _writer.Call((s, c) => s.SaveVocabulary(new SaveVocabularyRequest { StoryId = storyId, Vocabulary = vocabulary }, c));
}
