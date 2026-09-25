using Grpc.Core;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class LibraryTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    [Fact]
    public async Task A_rename_rewrites_every_mention_across_scenes_and_shifts_the_ones_after_it()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var radio = await _writer.CreatePropAsync(story.Id, "Radio");
        const string first = "@Mara tunes the @Radio. @Mara sighs.";
        await _writer.WriteFirstSceneAsync(
            story,
            Heading(),
            Action(first, Mention(first, "@Mara", mara), Mention(first, "@Radio", radio), Mention(first, "@Mara", mara, 1)));
        const string second = "Later, @Mara slept.";
        var later = await _writer.CreateSceneAsync(
            story.Containers[0].Id, 1, "Later", Heading(), Beat(SceneElementType.Narration, second, Mention(second, "@Mara", mara)));

        await _writer.Call((s, c) => s.UpdateCharacter(
            new UpdateCharacterRequest { CharacterId = mara.Id, Name = "  Mara Voss ", Kind = "Human", Hue = 25 }, c));

        var opening = (await _writer.GetSceneAsync(story.Containers[0].Scenes[0].Id)).Elements[1];
        Assert.Equal("@Mara Voss tunes the @Radio. @Mara Voss sighs.", opening.Text);
        Assert.Equal(["@Mara Voss", "@Radio", "@Mara Voss"], Slices(opening));
        Assert.Equal([0, 21, 29], opening.Mentions.Select(m => m.Offset));

        var elsewhere = (await _writer.GetSceneAsync(later.Id)).Elements[1];
        Assert.Equal("Later, @Mara Voss slept.", elsewhere.Text);
        Assert.Equal(["@Mara Voss"], Slices(elsewhere));

        var library = await _writer.GetStoryAsync(story.Id);
        Assert.Equal("Mara Voss", Assert.Single(library.Characters).Name);
    }

    [Fact]
    public async Task Recasing_a_name_rewrites_its_mentions_too()
    {
        var story = await _writer.CreateStoryAsync();
        var map = await _writer.CreatePropAsync(story.Id, "map");
        const string text = "She unrolls the @map.";
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action(text, Mention(text, "@map", map)));

        await _writer.Call((s, c) => s.UpdateProp(new UpdatePropRequest { PropId = map.Id, Name = "Map" }, c));

        Assert.Equal("She unrolls the @Map.", (await _writer.GetSceneAsync(scene.Id)).Elements[1].Text);
    }

    [Fact]
    public async Task Names_clash_ignoring_case_and_space_but_only_within_one_kind()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var bolt = await _writer.CreateCharacterAsync(story.Id, "Bolt", "Robot");

        var duplicate = await Assert.ThrowsAsync<RpcException>(() => _writer.CreateCharacterAsync(story.Id, "  mara "));
        Assert.Equal(StatusCode.AlreadyExists, duplicate.StatusCode);
        Assert.Equal("There’s already a character called “Mara”.", duplicate.Status.Detail);

        var rename = await Assert.ThrowsAsync<RpcException>(() => _writer.Call((s, c) => s.UpdateCharacter(
            new UpdateCharacterRequest { CharacterId = bolt.Id, Name = "MARA", Kind = "Robot", Hue = 25 }, c)));
        Assert.Equal(StatusCode.AlreadyExists, rename.StatusCode);

        var location = await _writer.CreateLocationAsync(story.Id, "MARA");
        Assert.Equal("MARA", location.Name);
        await _writer.Call((s, c) => s.UpdateCharacter(
            new UpdateCharacterRequest { CharacterId = mara.Id, Name = "MARA", Kind = "Human", Hue = 25 }, c));

        var library = await _writer.GetStoryAsync(story.Id);
        Assert.Equal(["Bolt", "MARA"], library.Characters.Select(c => c.Name));
    }

    [Fact]
    public async Task A_character_needs_a_kind_from_the_vocabulary_and_a_hue_in_range()
    {
        var story = await _writer.CreateStoryAsync();

        var kind = await Assert.ThrowsAsync<RpcException>(() => _writer.CreateCharacterAsync(story.Id, "Mara", "Ghost"));
        Assert.Equal(StatusCode.InvalidArgument, kind.StatusCode);

        var hue = await Assert.ThrowsAsync<RpcException>(() => _writer.Call((s, c) => s.CreateCharacter(
            new CreateCharacterRequest { StoryId = story.Id, Name = "Mara", Kind = "Human", Hue = 360 }, c)));
        Assert.Equal(StatusCode.InvalidArgument, hue.StatusCode);

        var robot = await _writer.CreateCharacterAsync(story.Id, "Bolt", "robot");
        Assert.Equal("Robot", robot.Kind);
    }

    [Fact]
    public async Task Deleting_a_location_clears_headings_and_unlinks_mentions_but_keeps_the_text()
    {
        var story = await _writer.CreateStoryAsync();
        var docks = await _writer.CreateLocationAsync(story.Id, "Harbor Docks");
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        const string text = "@Mara steps off at the @Harbor Docks.";
        var scene = await _writer.WriteFirstSceneAsync(
            story,
            Heading(docks.Id, "NIGHT"),
            Action(text, Mention(text, "@Mara", mara), Mention(text, "@Harbor Docks", docks)));
        Assert.Equal(docks.Id, (await _writer.GetStoryAsync(story.Id)).Containers[0].Scenes[0].OpeningLocationId);

        await _writer.Call((s, c) => s.DeleteLocation(new DeleteLocationRequest { LocationId = docks.Id }, c));

        var after = await _writer.GetSceneAsync(scene.Id);
        Assert.False(after.Elements[0].HasLocationId);
        Assert.Equal("NIGHT", after.Elements[0].TimeOfDay);
        Assert.Equal(text, after.Elements[1].Text);
        Assert.Equal(["@Mara"], Slices(after.Elements[1]));
        var summary = (await _writer.GetStoryAsync(story.Id)).Containers[0].Scenes[0];
        Assert.False(summary.HasOpeningLocationId);
        Assert.Equal("NIGHT", summary.OpeningTimeOfDay);
    }

    [Fact]
    public async Task Deleting_a_character_clears_its_dialogue()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Dialogue(mara.Id, "One bar.", "whispering"));

        await _writer.Call((s, c) => s.DeleteCharacter(new DeleteCharacterRequest { CharacterId = mara.Id }, c));

        var line = (await _writer.GetSceneAsync(scene.Id)).Elements[1];
        Assert.False(line.HasSpeakerId);
        Assert.Equal("whispering", line.Parenthetical);
    }
}
