using Grpc.Core;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class SceneSaveTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    [Fact]
    public async Task Saving_upserts_rows_by_id_in_order_and_deletes_the_missing_ones()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var radio = await _writer.CreatePropAsync(story.Id, "Radio");
        const string before = "@Mara listens.";
        var scene = await _writer.WriteFirstSceneAsync(
            story, Heading(), Action(before, Mention(before, "@Mara", mara)), Transition("CUT TO"));
        var (heading, beat, transition) = (scene.Elements[0], scene.Elements[1], scene.Elements[2]);

        const string after = "She turns the @Radio.";
        beat.Text = after;
        beat.Mentions.Clear();
        beat.Mentions.Add(Mention(after, "@Radio", radio));
        var added = Dialogue(mara.Id, "Come on.", "(whispering)");
        scene.Title = "   ";
        scene.Elements.Clear();
        scene.Elements.AddRange([transition, added, beat]);
        await _writer.SaveSceneAsync(scene);

        var saved = await _writer.GetSceneAsync(scene.Id);
        Assert.Equal("Untitled scene", saved.Title);
        Assert.Equal([transition.Id, added.Id, beat.Id], saved.Elements.Select(e => e.Id));
        Assert.Equal([0, 1, 2], saved.Elements.Select(e => e.Position));
        Assert.DoesNotContain(saved.Elements, e => e.Id == heading.Id);
        Assert.Equal("whispering", saved.Elements[1].Parenthetical);
        Assert.Equal(after, saved.Elements[2].Text);
        Assert.Equal(["@Radio"], Slices(saved.Elements[2]));
        Assert.Equal(radio.Id, saved.Elements[2].Mentions[0].PropId);
    }

    [Fact]
    public async Task Saving_the_same_rows_twice_changes_nothing()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        const string text = "@Mara waits.";
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action(text, Mention(text, "@Mara", mara)));

        await _writer.SaveSceneAsync(scene);

        Assert.Equal(scene, await _writer.GetSceneAsync(scene.Id));
    }

    [Fact]
    public async Task A_title_over_255_characters_is_refused()
    {
        var story = await _writer.CreateStoryAsync();
        var scene = await _writer.GetSceneAsync(story.Containers[0].Scenes[0].Id);
        scene.Title = new string('x', 256);

        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.SaveSceneAsync(scene));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal("Titles are limited to 255 characters.", error.Status.Detail);
    }

    [Theory]
    [InlineData("heading with text")]
    [InlineData("heading with a speaker")]
    [InlineData("action with a location")]
    [InlineData("action with a time of day")]
    [InlineData("narration with a transition")]
    [InlineData("dialogue with a time of day")]
    [InlineData("transition with text")]
    [InlineData("transition with a parenthetical")]
    [InlineData("no type")]
    [InlineData("unknown time of day")]
    [InlineData("unknown transition")]
    [InlineData("location from another story")]
    [InlineData("repeated id")]
    public async Task Rows_that_set_fields_their_type_does_not_use_are_refused(string problem)
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var docks = await _writer.CreateLocationAsync(story.Id, "Docks");
        var other = await _writer.CreateStoryAsync("Another");
        var elsewhere = await _writer.CreateLocationAsync(other.Id, "Elsewhere");
        var row = problem switch
        {
            "heading with text" => With(Heading(docks.Id), r => r.Text = "INT."),
            "heading with a speaker" => With(Heading(), r => r.SpeakerId = mara.Id),
            "action with a location" => With(Action("Rain."), r => r.LocationId = docks.Id),
            "action with a time of day" => With(Action("Rain."), r => r.TimeOfDay = "DAY"),
            "narration with a transition" => With(Beat(SceneElementType.Narration, "Once."), r => r.Transition = "CUT TO"),
            "dialogue with a time of day" => With(Dialogue(mara.Id, "Hi."), r => r.TimeOfDay = "DAY"),
            "transition with text" => With(Transition("CUT TO"), r => r.Text = "CUT"),
            "transition with a parenthetical" => With(Transition("CUT TO"), r => r.Parenthetical = "beat"),
            "no type" => With(Action("Rain."), r => r.Type = SceneElementType.Unspecified),
            "unknown time of day" => Heading(docks.Id, "TEATIME"),
            "unknown transition" => Transition("WIPE TO"),
            "location from another story" => Heading(elsewhere.Id),
            _ => Heading(),
        };
        var scene = await _writer.GetSceneAsync(story.Containers[0].Scenes[0].Id);
        scene.Elements.Add(row);
        if (problem == "repeated id")
        {
            scene.Elements.Add(With(Action("Again."), r => r.Id = row.Id));
        }

        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.SaveSceneAsync(scene));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Theory]
    [InlineData("outside the text")]
    [InlineData("not at an @")]
    [InlineData("shorter than two")]
    [InlineData("overlapping")]
    [InlineData("without a target")]
    [InlineData("target from another story")]
    [InlineData("on a heading")]
    public async Task Invalid_mentions_are_refused(string problem)
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var other = await _writer.CreateStoryAsync("Another");
        var stranger = await _writer.CreateCharacterAsync(other.Id, "Mara");
        const string text = "@Mara waits.";
        var mention = Mention(text, "@Mara", mara);
        var row = problem switch
        {
            "outside the text" => Action(text, With(mention, m => m.Offset = text.Length - 2)),
            "not at an @" => Action(text, With(mention, m => m.Offset = 1)),
            "shorter than two" => Action(text, With(mention, m => m.Length = 1)),
            "overlapping" => Action(text, mention, With(Mention(text, "@Mara", mara), m => m.Length = 3)),
            "without a target" => Action(text, With(mention, m => m.ClearTarget())),
            "target from another story" => Action(text, Mention(text, "@Mara", stranger)),
            _ => With(Heading(), r => r.Mentions.Add(mention)),
        };

        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.WriteFirstSceneAsync(story, Heading(), row));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Fact]
    public async Task Creating_a_scene_inserts_it_and_moves_later_scenes_down()
    {
        var story = await _writer.CreateStoryAsync();
        var act = story.Containers[0];
        var last = await _writer.CreateSceneAsync(act.Id, 5, "Last", Heading());
        var middle = await _writer.CreateSceneAsync(act.Id, 1, "Middle", Heading(null, "LATER"));
        var first = await _writer.CreateSceneAsync(act.Id, 0, "First", Heading());

        var outline = (await _writer.GetStoryAsync(story.Id)).Containers[0].Scenes;
        Assert.Equal(["First", "Untitled scene", "Middle", "Last"], outline.Select(s => s.Title));
        Assert.Equal([0, 1, 2, 3], outline.Select(s => s.Position));
        Assert.Equal([first.Id, act.Scenes[0].Id, middle.Id, last.Id], outline.Select(s => s.Id));
        Assert.Equal("LATER", outline[2].OpeningTimeOfDay);
        Assert.Equal(middle.Elements[0].Id, (await _writer.GetSceneAsync(middle.Id)).Elements[0].Id);
    }

    private static T With<T>(T value, Action<T> change)
    {
        change(value);
        return value;
    }
}
