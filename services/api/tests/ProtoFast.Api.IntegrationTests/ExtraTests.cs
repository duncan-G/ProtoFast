using Grpc.Core;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class ExtraTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    [Fact]
    public async Task Extras_are_library_entries_with_a_description_and_a_wardrobe()
    {
        var story = await _writer.CreateStoryAsync();
        var regulars = await _writer.CreateExtraAsync(story.Id, "Pub regulars", "A dozen drinkers in their sixties.", "flat caps", headcount: 12);
        Assert.Equal(12, regulars.Headcount);

        var nobody = await Assert.ThrowsAsync<RpcException>(() => _writer.CreateExtraAsync(story.Id, "Ghosts", headcount: 0));
        Assert.Equal(StatusCode.InvalidArgument, nobody.StatusCode);

        var duplicate = await Assert.ThrowsAsync<RpcException>(() => _writer.CreateExtraAsync(story.Id, " pub REGULARS"));
        Assert.Equal(StatusCode.AlreadyExists, duplicate.StatusCode);
        Assert.Equal("There’s already an extra called “Pub regulars”.", duplicate.Status.Detail);

        var updated = (await _writer.Call((s, c) => s.UpdateExtra(
            new UpdateExtraRequest { ExtraId = regulars.Id, Name = "Regulars", Description = "  Old drinkers.  " }, c))).Extra;
        Assert.Equal(("Regulars", "Old drinkers.", false, false), (updated.Name, updated.Description, updated.HasWardrobe, updated.HasHeadcount));

        var listed = Assert.Single((await _writer.GetStoryAsync(story.Id)).Extras);
        Assert.Equal((regulars.Id, "Regulars"), (listed.Id, listed.Name));

        await _writer.Call((s, c) => s.DeleteExtra(new DeleteExtraRequest { ExtraId = regulars.Id }, c));
        Assert.Empty((await _writer.GetStoryAsync(story.Id)).Extras);
    }

    [Fact]
    public async Task The_author_puts_extras_in_a_scene_and_takes_them_out_of_a_frame()
    {
        var story = await _writer.CreateStoryAsync();
        var crowd = await _writer.CreateExtraAsync(story.Id, "Crowd");
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action("A crowd gathers."), Action("It thins out."));
        var direction = new SceneDirection { SceneId = scene.Id };
        direction.Extras.Add(new SceneExtra { ExtraId = crowd.Id });
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[0].Id, LastElementId = scene.Elements[1].Id });
        direction.Frames.Add(new Frame
        {
            FirstElementId = scene.Elements[2].Id,
            LastElementId = scene.Elements[2].Id,
            Extras = { new FrameExtra { ExtraId = crowd.Id, OnScreen = false } },
        });

        var saved = await _writer.SaveDirectionAsync(direction);

        var row = Assert.Single(saved.Extras);
        Assert.Equal((crowd.Id, true, true), (row.ExtraId, row.OnScreen, row.Edited));
        Assert.Empty(saved.Frames[0].Extras);
        Assert.Equal([(crowd.Id, false)], saved.Frames[1].Extras.Select(e => (e.ExtraId, e.OnScreen)));

        var stranger = new SceneDirection(saved);
        stranger.Extras.Add(new SceneExtra { ExtraId = Guid.NewGuid().ToString() });
        var invalid = await Assert.ThrowsAsync<RpcException>(() => _writer.SaveDirectionAsync(stranger));
        Assert.Equal(StatusCode.InvalidArgument, invalid.StatusCode);

        // Deleting the extra takes it out of the scene and its frames.
        await _writer.Call((s, c) => s.DeleteExtra(new DeleteExtraRequest { ExtraId = crowd.Id }, c));
        var after = await _writer.GetDirectionAsync(scene.Id);
        Assert.Empty(after.Extras);
        Assert.All(after.Frames, f => Assert.Empty(f.Extras));
    }

    [Fact]
    public async Task Directing_the_scene_finds_its_extras_and_adds_the_ones_the_library_lacks()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var regulars = await _writer.CreateExtraAsync(story.Id, "Pub regulars", "Old drinkers.");
        const string text = "@Mara pushes through the drinkers to the bar. The barman ignores her.";
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action(text, Mention(text, "@Mara", mara)), Dialogue(mara.Id, "Whisky."));
        // A cast member's name is never an extra; a name given twice is one extra.
        // Nothing in the library is left for the presence pass to find, so it doesn't ask.
        _writer.ScriptModel(
            """{"extras":[{"name":"pub regulars"},{"name":"Barman","headcount":1,"description":"A heavy-set man in his fifties.","wardrobe":"rolled shirtsleeves, apron"},{"name":"Mara"},{"name":"barman"}]}""",
            """{"storyDay":1}""",
            """{"looks":[{"name":"Mara"}],"props":[]}""",
            """{"cues":[{"line":"e3","performance":"Whisky."}]}""",
            """{"frames":[{"from":"e1","to":"e2","shot":"wide","visible":"Mara squeezes past the Pub regulars to the Barman.","characters":[{"name":"Mara","screen":"left"}],"extras":["Pub regulars","Barman"]},{"from":"e3","to":"e3","shot":"close","visible":"Mara orders from the Barman.","characters":[{"name":"Mara","screen":"left"}],"extras":["Barman"]}]}""");

        var reply = await _writer.Direct((d, c) => d.Direct(new DirectRequest { SceneId = scene.Id, Scene = true }, c));

        var barman = Assert.Single(reply.Extras, e => e.Name == "Barman");
        Assert.Equal(("A heavy-set man in his fifties.", "rolled shirtsleeves, apron", 1), (barman.Description, barman.Wardrobe, barman.Headcount));
        Assert.Equal(["Barman", "Pub regulars"], (await _writer.GetStoryAsync(story.Id)).Extras.Select(e => e.Name));
        var direction = reply.Direction;
        Assert.Equal(new[] { regulars.Id, barman.Id }.Order(), direction.Extras.Select(e => e.ExtraId).Order());
        Assert.All(direction.Extras, e => Assert.False(e.Edited));
        // Both are in the scene, so only the frame that leaves the regulars out says so.
        Assert.Empty(direction.Frames[0].Extras);
        Assert.Equal([(regulars.Id, false)], direction.Frames[1].Extras.Select(e => (e.ExtraId, e.OnScreen)));

        var prompts = _writer.ModelPrompts();
        Assert.Contains("Extras in the library:\n- Pub regulars: Old drinkers.", prompts[0]);
        Assert.Contains("Extras:\n- Pub regulars: Old drinkers.\n- Barman (one person): A heavy-set man in his fifties. Wearing: rolled shirtsleeves, apron", prompts[4]);

        // The next scene reuses the barman rather than adding another.
        var next = await _writer.CreateSceneAsync(story.Containers[0].Id, 1, "Closing time", Heading(), Action("The barman wipes the counter."));
        _writer.ScriptModel("""{"extras":[{"name":"Barman","description":"someone else"}]}""");
        var again = await _writer.Direct((d, c) => d.Direct(new DirectRequest { SceneId = next.Id, Extras = true }, c));
        Assert.Equal([barman.Id], again.Direction.Extras.Select(e => e.ExtraId));
        Assert.Equal(2, again.Extras.Count);
        Assert.Equal("A heavy-set man in his fifties.", again.Extras.Single(e => e.Id == barman.Id).Description);
    }

    [Fact]
    public async Task Finding_extras_keeps_the_ones_the_author_added_unless_told_to_replace_them()
    {
        var story = await _writer.CreateStoryAsync();
        var crowd = await _writer.CreateExtraAsync(story.Id, "Crowd");
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action("Rain on the square."));
        var direction = new SceneDirection { SceneId = scene.Id };
        direction.Extras.Add(new SceneExtra { ExtraId = crowd.Id });
        await _writer.SaveDirectionAsync(direction);
        _writer.ScriptModel(
            """{"extras":[]}""",
            """{"storyDay":1}""",
            """{"frames":[{"from":"e1","to":"e2","shot":"wide","visible":"Rain on the empty square."}]}""");

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Scene = true });

        Assert.Equal([crowd.Id], directed.Extras.Select(e => e.ExtraId));
        // The frames answer said nothing about extras, so the frame follows the scene.
        Assert.Empty(Assert.Single(directed.Frames).Extras);

        _writer.ScriptModel("""{"extras":[]}""");
        var kept = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Extras = true });
        Assert.Equal([crowd.Id], kept.Extras.Select(e => e.ExtraId));

        _writer.ScriptModel("""{"extras":[]}""");
        var replaced = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Extras = true, ReplaceEdited = true });
        Assert.Empty(replaced.Extras);
    }

    [Fact]
    public async Task Directing_frames_finds_the_extras_first_and_one_frame_asks_only_when_the_scene_has_none()
    {
        var story = await _writer.CreateStoryAsync();
        var bryan = await _writer.CreateCharacterAsync(story.Id, "Bryan");
        const string text = "@Bryan tries to lock eyes with a passing nurse.";
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action(text, Mention(text, "@Bryan", bryan)));
        _writer.ScriptModel(
            """{"extras":[{"name":"Nurses","description":"Two nurses in their thirties.","wardrobe":"blue scrubs"}]}""",
            """{"frames":[{"from":"e1","to":"e2","shot":"wide","visible":"Bryan turns to the Nurses as they pass.","characters":[{"name":"Bryan","screen":"left"}],"extras":["Nurses"]}]}""");

        var framed = await _writer.Direct((d, c) => d.Direct(new DirectRequest { SceneId = scene.Id, Frames = true }, c));

        var nurses = Assert.Single(framed.Extras);
        Assert.Equal([nurses.Id], framed.Direction.Extras.Select(e => e.ExtraId));
        Assert.Empty(Assert.Single(framed.Direction.Frames).Extras);
        Assert.Contains("Extras:\n- Nurses: Two nurses in their thirties. Wearing: blue scrubs", _writer.ModelPrompts()[1]);

        // The scene has its extras now, so directing one frame goes straight to the frame.
        _writer.ScriptModel("""{"shot":"close","visible":"Bryan watches the Nurses go.","characters":[{"name":"Bryan","screen":"left"}],"extras":["Nurses"]}""");
        await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, FrameId = framed.Direction.Frames[0].Id });
        Assert.StartsWith("One frame of the scene", _writer.ModelPrompts()[^1]);
        Assert.Equal(3, _writer.ModelPrompts().Count);
    }

    [Fact]
    public async Task Describing_extras_writes_their_description_and_wardrobe_from_the_scenes_they_are_in()
    {
        var story = await _writer.CreateStoryAsync();
        var nurses = await _writer.CreateExtraAsync(story.Id, "Night-shift nurses");
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action("Nurses hurry past in blue scrubs."));
        var direction = new SceneDirection { SceneId = scene.Id };
        direction.Extras.Add(new SceneExtra { ExtraId = nurses.Id });
        await _writer.SaveDirectionAsync(direction);
        _writer.ScriptModel("""{"headcount":3,"description":"Three tired nurses in their twenties and thirties.","wardrobe":"blue scrubs, lanyards"}""");

        var reply = await _writer.Direct((d, c) => d.Describe(new DescribeRequest { ExtraId = nurses.Id }, c));

        Assert.Equal(("Three tired nurses in their twenties and thirties.", "blue scrubs, lanyards", 3), (reply.Extra.Description, reply.Extra.Wardrobe, reply.Extra.Headcount));
        Assert.Contains("Nurses hurry past in blue scrubs.", _writer.ModelPrompts().Single());
    }
}
