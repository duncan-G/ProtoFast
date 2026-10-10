using Grpc.Core;
using ProtoFast.Api.Services.Directing;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class DirectionTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    /// <summary>A heading, an action, two lines (Mara, then Bolt) and a transition, with Mara wearing a "work suit".</summary>
    private async Task<(Story Story, Scene Scene, Character Mara, Character Bolt, Prop Radio, Wardrobe Suit)> DirectedSceneAsync()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var bolt = await _writer.CreateCharacterAsync(story.Id, "Bolt", "Robot");
        var radio = await _writer.CreatePropAsync(story.Id, "Radio");
        var docks = await _writer.CreateLocationAsync(story.Id, "Docks");
        var sheet = await _writer.Call((s, c) => s.UpdateCharacterSheet(new UpdateCharacterSheetRequest
        {
            CharacterId = mara.Id,
            Description = "A dock worker who hears things.",
            Wardrobes = { new Wardrobe { Label = "work suit", Description = "oil-stained overalls" } },
        }, c));
        const string action = "@Mara turns the @Radio.";
        var scene = await _writer.WriteFirstSceneAsync(
            story,
            Heading(docks.Id, "NIGHT"),
            Action(action, Mention(action, "@Mara", mara), Mention(action, "@Radio", radio)),
            Dialogue(mara.Id, "Don't move."),
            Dialogue(bolt.Id, "I never do."),
            Transition("CUT TO"));
        return (story, scene, mara, bolt, radio, sheet.Character.Wardrobes[0]);
    }

    private static SceneDirection Direction(Scene scene, Character mara, Character bolt, Prop radio, Wardrobe suit)
    {
        var direction = new SceneDirection { SceneId = scene.Id, StoryDay = 2, Shot = "medium", Angle = "eye level" };
        direction.Frames.Add(new Frame
        {
            FirstElementId = scene.Elements[0].Id,
            LastElementId = scene.Elements[1].Id,
            Shot = "Wide",
            Movement = "push in",
            Beat = "Mara at the radio.",
            LocationChange = "Only the radio's glow lights the frame.",
            Placements =
            {
                new Placement { CharacterId = mara.Id, Screen = "left", Pose = "crouched", Expression = "alert", AppearanceChange = "soot on her cheek" },
                new Placement { CharacterId = bolt.Id, OnScreen = false },
            },
            Props = { new FrameProp { PropId = radio.Id, InView = true } },
        });
        direction.Frames.Add(new Frame
        {
            FirstElementId = scene.Elements[2].Id,
            LastElementId = scene.Elements[4].Id,
            Shot = "over-the-shoulder",
            Beat = "Bolt answers.",
            Placements =
            {
                new Placement { CharacterId = mara.Id, Screen = "left", Pose = "standing", Expression = "wary" },
                new Placement { CharacterId = bolt.Id, Screen = "right", Pose = "still", Expression = "blank" },
            },
        });
        direction.Cues.Add(new Cue { ElementId = scene.Elements[2].Id, Performance = "[whispers] Don't move.", GapBeforeMs = 400 });
        direction.Cues.Add(new Cue { ElementId = scene.Elements[3].Id, Performance = "I never [deadpan] do.", Interrupted = true });
        direction.Appearances.Add(new SceneAppearance
        {
            CharacterId = mara.Id,
            WardrobeId = suit.Id,
            WardrobeChange = "sleeves rolled",
            AppearanceChange = "wet hair",
            ChangeReason = "She came in from the rain.",
            ChangeElementId = scene.Elements[1].Id,
        });
        direction.PropStates.Add(new ScenePropState { PropId = radio.Id, State = { "crackling" }, InView = false });
        return direction;
    }

    [Fact]
    public async Task Saving_a_direction_round_trips_in_scene_order_and_marks_the_rows_edited()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var direction = Direction(scene, mara, bolt, radio, suit);
        // Sent out of order: the saved frames follow the scene.
        var reversed = new SceneDirection(direction);
        reversed.Frames.Clear();
        reversed.Frames.AddRange(direction.Frames.Reverse());

        var saved = await _writer.SaveDirectionAsync(reversed);

        Assert.Equal(saved, await _writer.GetDirectionAsync(scene.Id));
        Assert.Equal(2, saved.StoryDay);
        Assert.Equal([scene.Elements[0].Id, scene.Elements[2].Id], saved.Frames.Select(p => p.FirstElementId));
        Assert.Equal([0, 1], saved.Frames.Select(p => p.Position));
        Assert.All(saved.Frames.Concat<object>(saved.Cues).Concat(saved.Appearances).Concat(saved.PropStates), row =>
            Assert.NotEqual(Guid.Empty, Guid.Parse((string)row.GetType().GetProperty("Id")!.GetValue(row)!)));
        Assert.Equal(("medium", "eye level", false), (saved.Shot, saved.Angle, saved.HasMovement));
        Assert.Equal("wide", saved.Frames[0].Shot);
        Assert.Equal("push in", saved.Frames[0].Movement);
        Assert.False(saved.Frames[1].HasAngle);
        Assert.Equal("Only the radio's glow lights the frame.", saved.Frames[0].LocationChange);
        var maraInFirst = saved.Frames[0].Placements.Single(p => p.CharacterId == mara.Id);
        Assert.Equal(("soot on her cheek", false), (maraInFirst.AppearanceChange, maraInFirst.HasOnScreen));
        Assert.False(saved.Frames[0].Placements.Single(p => p.CharacterId == bolt.Id).OnScreen);
        Assert.Equal(["left", "right"], saved.Frames[1].Placements.OrderBy(p => p.Screen).Select(p => p.Screen));
        Assert.Equal([(radio.Id, true)], saved.Frames[0].Props.Select(p => (p.PropId, p.InView)));
        Assert.Equal("[whispers] Don't move.", saved.Cues[0].Performance);
        Assert.True(saved.Cues[1].Interrupted);
        Assert.Equal(suit.Id, saved.Appearances[0].WardrobeId);
        Assert.Equal(("sleeves rolled", "wet hair", true), (saved.Appearances[0].WardrobeChange, saved.Appearances[0].AppearanceChange, saved.Appearances[0].OnScreen));
        Assert.Equal(["crackling"], saved.PropStates[0].State);
        Assert.False(saved.PropStates[0].InView);
        Assert.All(saved.Frames, p => Assert.True(p.Edited));
        Assert.All(saved.Cues, c => Assert.True(c.Edited));
        Assert.True(saved.Appearances[0].Edited);
        Assert.True(saved.PropStates[0].Edited);
    }

    [Fact]
    public async Task Rows_left_out_of_a_save_are_deleted_and_the_rest_keep_their_ids()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var saved = await _writer.SaveDirectionAsync(Direction(scene, mara, bolt, radio, suit));

        var next = new SceneDirection(saved);
        next.Cues.RemoveAt(1);
        next.Frames.RemoveAt(0);
        next.PropStates.Clear();
        next.Cues[0].GapBeforeMs = 900;
        var again = await _writer.SaveDirectionAsync(next);

        Assert.Equal([saved.Cues[0].Id], again.Cues.Select(c => c.Id));
        Assert.Equal(900, again.Cues[0].GapBeforeMs);
        Assert.Equal([saved.Frames[1].Id], again.Frames.Select(p => p.Id));
        Assert.Equal(0, again.Frames[0].Position);
        Assert.Empty(again.PropStates);
        Assert.Single(again.Appearances);
    }

    [Theory]
    [InlineData("unknown tag")]
    [InlineData("too many tags")]
    [InlineData("cue on a heading")]
    [InlineData("narrator on a dialogue line")]
    [InlineData("unknown camera angle")]
    [InlineData("two cues on one line")]
    [InlineData("overlapping frames")]
    [InlineData("frame ending before it starts")]
    [InlineData("unknown shot")]
    [InlineData("unknown screen side")]
    [InlineData("unknown facing")]
    [InlineData("unknown screen direction")]
    [InlineData("another character's wardrobe")]
    [InlineData("row from another scene")]
    [InlineData("story day zero")]
    public async Task Invalid_directions_are_refused(string problem)
    {
        var (story, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var other = await _writer.CreateSceneAsync(story.Containers[0].Id, 1, "Elsewhere", Heading(), Action("Rain."));
        var direction = Direction(scene, mara, bolt, radio, suit);
        switch (problem)
        {
            case "unknown tag":
                direction.Cues[0].Performance = "[wistfully] Don't move.";
                break;
            case "too many tags":
                direction.Cues[0].Performance = "[sad] [slowly] [sighs] Don't [pause] move.";
                break;
            case "cue on a heading":
                direction.Cues[0].ElementId = scene.Elements[0].Id;
                break;
            case "narrator on a dialogue line":
                direction.Cues[0].NarratorId = Guid.NewGuid().ToString();
                break;
            case "unknown camera angle":
                direction.Frames[0].Angle = "dutch";
                break;
            case "two cues on one line":
                direction.Cues[1].ElementId = direction.Cues[0].ElementId;
                break;
            case "overlapping frames":
                direction.Frames[1].FirstElementId = scene.Elements[1].Id;
                break;
            case "frame ending before it starts":
                direction.Frames[0].LastElementId = scene.Elements[0].Id;
                direction.Frames[0].FirstElementId = scene.Elements[1].Id;
                break;
            case "unknown shot":
                direction.Frames[0].Shot = "dutch angle";
                break;
            case "unknown screen side":
                direction.Frames[0].Placements[0].Screen = "middle";
                break;
            case "unknown facing":
                direction.Frames[0].Placements[0].Facing = "sideways";
                break;
            case "unknown screen direction":
                direction.Appearances[0].Travel = "toward camera";
                break;
            case "another character's wardrobe":
                direction.Appearances[0].CharacterId = bolt.Id;
                break;
            case "row from another scene":
                direction.Cues[0].ElementId = other.Elements[1].Id;
                break;
            case "story day zero":
                direction.StoryDay = 0;
                break;
        }

        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.SaveDirectionAsync(direction));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_line_takes_its_cue_and_the_frame_it_bounds_with_it()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var saved = await _writer.SaveDirectionAsync(Direction(scene, mara, bolt, radio, suit));

        scene.Elements.RemoveAt(4); // the transition, which ends the second frame
        scene.Elements.RemoveAt(3); // Bolt's line
        await _writer.SaveSceneAsync(scene);

        var after = await _writer.GetDirectionAsync(scene.Id);
        Assert.Equal([saved.Cues[0].Id], after.Cues.Select(c => c.Id));
        Assert.Equal([saved.Frames[0].Id], after.Frames.Select(p => p.Id));
        Assert.Single(after.Appearances);
    }

    [Fact]
    public async Task Saving_the_scene_and_its_direction_together_lands_a_changed_line_with_its_cue()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var direction = Direction(scene, mara, bolt, radio, suit);
        scene.Elements[2].Text = "Don't you move.";
        direction.Cues[0].Performance = "[whispers] Don't you move.";

        var saved = await _writer.Direct(async (d, c) =>
            (await d.SaveDirectedScene(new SaveDirectedSceneRequest { Scene = scene, Direction = direction }, c)).Direction);

        Assert.Equal("Don't you move.", (await _writer.GetSceneAsync(scene.Id)).Elements[2].Text);
        Assert.Equal("[whispers] Don't you move.", saved.Cues[0].Performance);
    }

    [Fact]
    public async Task Directing_a_line_assembles_its_performance_from_the_lines_own_words()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        await _writer.SaveDirectionAsync(Direction(scene, mara, bolt, radio, suit));
        var line = scene.Elements[3];
        _writer.ScriptModel("""
            Here you go: {"cues":[{"line":"e4","performance":"I [Sad] never [not-a-tag] do,[sighs]","gapBeforeMs":"250","interrupted":false}]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, CueElementId = line.Id });

        var cue = Assert.Single(directed.Cues, c => c.ElementId == line.Id);
        Assert.Equal("I [sad] never do. [sighs]", cue.Performance);
        Assert.Equal(250, cue.GapBeforeMs);
        Assert.False(cue.Interrupted);
        Assert.False(cue.Edited);
        Assert.True(Performances.SameWords(cue.Performance, line.Text));
        Assert.Contains("e4 BOLT: I never do.", _writer.ModelPrompts()[0]);
        // The other cue was the author's and is untouched.
        Assert.Equal("[whispers] Don't move.", Assert.Single(directed.Cues, c => c.ElementId == scene.Elements[2].Id).Performance);
    }

    [Fact]
    public async Task Directing_a_line_refuses_an_answer_that_rewrites_its_words()
    {
        var (_, scene, _, _, _, _) = await DirectedSceneAsync();
        var line = scene.Elements[3];
        _writer.ScriptModel("""{"cues":[{"line":"e4","performance":"I [sad] always do."}]}""");

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, CueElementId = line.Id }));

        Assert.Equal(DirectionErrors.ModelUnusable, error.Status.Detail);
    }

    [Fact]
    public async Task Directing_an_action_line_reads_it_as_written_without_asking_the_model()
    {
        var (_, scene, _, _, _, _) = await DirectedSceneAsync();

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, CueElementId = scene.Elements[1].Id });

        Assert.Equal("Mara turns the Radio.", Assert.Single(directed.Cues).Performance);
        Assert.Empty(_writer.ModelPrompts());
    }

    [Fact]
    public async Task Directing_every_cue_keeps_the_authors_edits_unless_told_to_replace_them()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        await _writer.SaveDirectionAsync(Direction(scene, mara, bolt, radio, suit));
        _writer.ScriptModel(
            """{"cues":[{"line":"e3","performance":"Don't [angry] move."},{"line":"e4","performance":"[calm] I never do."}]}""");

        var kept = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Cues = true });
        // Both lines are the author's, so the model is not asked; the action line is read as written.
        Assert.Equal(["Mara turns the Radio.", "[whispers] Don't move.", "I never [deadpan] do."], kept.Cues.Select(c => c.Performance));
        Assert.Empty(_writer.ModelPrompts());

        var replaced = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Cues = true, ReplaceEdited = true });
        Assert.Equal(["Mara turns the Radio.", "Don't [angry] move.", "[calm] I never do."], replaced.Cues.Select(c => c.Performance));
        Assert.Contains("Direct every dialogue line; narration and action are context.", _writer.ModelPrompts()[0]);
        Assert.All(replaced.Cues, c => Assert.False(c.Edited));
    }

    [Fact]
    public async Task Directing_frames_directs_the_authors_cut_and_covers_every_row_once()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var direction = Direction(scene, mara, bolt, radio, suit);
        direction.Frames.RemoveAt(1); // keep only the author's first frame, over e1–e2
        await _writer.SaveDirectionAsync(direction);
        // The model's first frame runs past the author's cut and the transition is left uncovered.
        _writer.ScriptModel("""{"extras":[]}""", """
            {"frames":[
              {"from":"e1","to":"e3","shot":"close","beat":"Mara leans in.","characters":[{"name":"Mara","screen":"Left","pose":"leaning","expression":"tense"}],"props":["Radio","Teapot"]},
              {"from":"e4","to":"e4","shot":"medium","beat":"Bolt replies.","characters":[{"name":"Bolt","screen":"right"},{"name":"Nobody","screen":"left"}]}
            ]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Frames = true });

        var ids = scene.Elements.Select(e => e.Id).ToList();
        Assert.Equal(
            [(ids[0], ids[1]), (ids[2], ids[2]), (ids[3], ids[4])],
            directed.Frames.Select(p => (p.FirstElementId, p.LastElementId)));
        Assert.Equal([0, 1, 2], directed.Frames.Select(p => p.Position));
        Assert.Contains("exactly the same from and to (e1–e2)", _writer.ModelPrompts()[1]);
        Assert.Equal(("Mara leans in.", "close"), (directed.Frames[0].Beat, directed.Frames[0].Shot));
        // The scene's shot stays the author's; what matches it is left to follow it.
        Assert.Equal("medium", directed.Shot);
        Assert.Equal("close", directed.Frames[1].Shot);
        Assert.False(directed.Frames[2].HasShot);
        var maraInSecond = directed.Frames[1].Placements.Single(p => p.CharacterId == mara.Id);
        Assert.Equal(("left", false), (maraInSecond.Screen, maraInSecond.HasOnScreen));
        Assert.False(directed.Frames[1].Placements.Single(p => p.CharacterId == bolt.Id).OnScreen);
        Assert.Empty(directed.Frames[1].Props.Where(p => !p.InView));
        Assert.Equal([radio.Id], directed.Frames[1].Props.Select(p => p.PropId));
        Assert.False(directed.Frames[2].Placements.Single(p => p.CharacterId == mara.Id).OnScreen);
        Assert.Equal("right", directed.Frames[2].Placements.Single(p => p.CharacterId == bolt.Id).Screen);
        Assert.Empty(directed.Frames[2].Props);
        Assert.All(directed.Frames, p => Assert.False(p.Edited));

        // Deleting the first frame moves the others up; moving is not editing them.
        var next = new SceneDirection(directed);
        next.Frames.RemoveAt(0);
        var shifted = await _writer.SaveDirectionAsync(next);
        Assert.Equal([0, 1], shifted.Frames.Select(p => p.Position));
        Assert.All(shifted.Frames, p => Assert.False(p.Edited));
    }

    [Fact]
    public async Task Directing_frames_when_the_authors_frames_cover_the_scene_still_directs_them()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var saved = await _writer.SaveDirectionAsync(Direction(scene, mara, bolt, radio, suit));
        _writer.ScriptModel("""{"extras":[]}""", """
            {"frames":[
              {"from":"e1","to":"e2","shot":"wide","beat":"The docks at night."},
              {"from":"e3","to":"e5","shot":"over-the-shoulder","beat":"They face off."}
            ]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Frames = true });

        Assert.Equal(
            saved.Frames.Select(p => (p.Id, p.FirstElementId, p.LastElementId)),
            directed.Frames.Select(p => (p.Id, p.FirstElementId, p.LastElementId)));
        Assert.Equal(["The docks at night.", "They face off."], directed.Frames.Select(p => p.Beat));
        Assert.All(directed.Frames, p => Assert.False(p.Edited));
    }

    [Fact]
    public async Task Directing_frames_closes_a_leading_gap_without_overlapping_frames()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var direction = Direction(scene, mara, bolt, radio, suit);
        direction.Frames.Clear();
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[2].Id, LastElementId = scene.Elements[3].Id });
        await _writer.SaveDirectionAsync(direction);
        // The model answers the author's cut only, leaving e1–e2 and e5 to be closed.
        _writer.ScriptModel("""{"extras":[]}""", """{"frames":[{"from":"e3","to":"e4","shot":"close","beat":"They whisper."}]}""");

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Frames = true });

        var frame = Assert.Single(directed.Frames);
        Assert.Equal((scene.Elements[0].Id, scene.Elements[4].Id), (frame.FirstElementId, frame.LastElementId));
        Assert.Equal("They whisper.", frame.Beat);
    }

    [Fact]
    public async Task The_camera_and_screen_directions_round_trip()
    {
        var (story, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var crowd = await _writer.CreateExtraAsync(story.Id, "Dockhands", "Three dockhands.", "hi-vis vests");
        var direction = Direction(scene, mara, bolt, radio, suit);
        direction.Extras.Add(new SceneExtra { ExtraId = crowd.Id });
        direction.Appearances[0].Travel = "Left to right";
        direction.Frames[0].Camera = "on the quay behind the radio, looking back at the gate";
        var placement = direction.Frames[0].Placements[0];
        (placement.Facing, placement.Moving, placement.Looking) = ("three-quarter right", "toward camera, left to right", "the gate");
        direction.Frames[0].Extras.Add(new FrameExtra { ExtraId = crowd.Id, Screen = "right", Pose = "the nearest one, coiling a rope" });

        var saved = await _writer.SaveDirectionAsync(direction);

        Assert.Equal("left to right", saved.Appearances.Single(a => a.CharacterId == mara.Id).Travel);
        Assert.Equal("on the quay behind the radio, looking back at the gate", saved.Frames[0].Camera);
        var maraFirst = saved.Frames[0].Placements.Single(p => p.CharacterId == mara.Id);
        Assert.Equal(("three-quarter right", "toward camera, left to right", "the gate"), (maraFirst.Facing, maraFirst.Moving, maraFirst.Looking));
        var staged = Assert.Single(saved.Frames[0].Extras);
        Assert.Equal(("right", "the nearest one, coiling a rope", false), (staged.Screen, staged.Pose, staged.HasOnScreen));
    }

    [Fact]
    public async Task Directing_frames_stores_the_camera_each_persons_screen_direction_and_the_scenes_travel()
    {
        var (story, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var crowd = await _writer.CreateExtraAsync(story.Id, "Dockhands", "Three dockhands.", "hi-vis vests");
        var direction = Direction(scene, mara, bolt, radio, suit);
        direction.Frames.Clear();
        direction.Extras.Add(new SceneExtra { ExtraId = crowd.Id });
        await _writer.SaveDirectionAsync(direction);
        _writer.ScriptModel("""{"extras":[]}""", """
            {"travel":{"Mara":"screen left to right"},
             "frames":[{"from":"e1","to":"e5","shot":"wide","camera":"inside the warehouse, looking back at the gate",
               "visible":"Mara is half through the gate, a palm on it; Bolt waits screen right.",
               "characters":[
                 {"name":"Mara","screen":"left","facing":"three quarter right","moving":"towards the camera and left to right","looking":"Bolt"},
                 {"name":"Bolt","screen":"right","facing":"rear three-quarter left","moving":"stationary"}],
               "extras":[{"name":"Dockhands","screen":"right","pose":"the nearest one, coiling a rope"}]}]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Frames = true });

        var frame = Assert.Single(directed.Frames);
        Assert.Equal("inside the warehouse, looking back at the gate", frame.Camera);
        var maraStaged = frame.Placements.Single(p => p.CharacterId == mara.Id);
        Assert.Equal(("three-quarter right", "toward camera, left to right", "Bolt"), (maraStaged.Facing, maraStaged.Moving, maraStaged.Looking));
        var boltStaged = frame.Placements.Single(p => p.CharacterId == bolt.Id);
        Assert.Equal(("back three-quarter left", "still"), (boltStaged.Facing, boltStaged.Moving));
        // On screen as the scene has them, so the row is kept only for its staging.
        var staged = Assert.Single(frame.Extras);
        Assert.Equal(("right", "the nearest one, coiling a rope", false), (staged.Screen, staged.Pose, staged.HasOnScreen));
        Assert.Equal("left to right", directed.Appearances.Single(a => a.CharacterId == mara.Id).Travel);
        Assert.All(directed.Appearances.Where(a => a.CharacterId == bolt.Id), a => Assert.Equal("", a.Travel));
        Assert.Equal(2, _writer.ModelPrompts().Count);
    }

    [Fact]
    public async Task Directing_frames_asks_again_when_a_cut_reverses_someone_or_swaps_two_peoples_sides()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var direction = Direction(scene, mara, bolt, radio, suit);
        direction.Frames.Clear();
        await _writer.SaveDirectionAsync(direction);
        _writer.ScriptModel(
            """{"extras":[]}""",
            """
            {"travel":{"Mara":"left to right"},
             "frames":[
               {"from":"e1","to":"e2","shot":"wide","visible":"Mara walks toward Bolt.","characters":[{"name":"Mara","screen":"left","moving":"left to right"},{"name":"Bolt","screen":"right"}]},
               {"from":"e3","to":"e5","shot":"medium","visible":"Mara faces Bolt.","characters":[{"name":"Mara","screen":"right","facing":"three-quarter right","moving":"away from camera, right to left"},{"name":"Bolt","screen":"left"}]}]}
            """,
            """{"frames":[{"frame":"f1","camera":"behind Bolt's shoulder","visible":"Mara stops in front of Bolt.","characters":[{"name":"Mara","screen":"left","moving":"still"},{"name":"Bolt","screen":"right","facing":"screen left"}]}]}""");

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Frames = true });

        var asked = _writer.ModelPrompts()[2];
        Assert.Contains("These frames break screen direction", asked);
        Assert.Contains("Screen directions in this scene, keep them: Mara left to right.", asked);
        Assert.Contains("f1 rows e3–e5, medium shot; on screen: Mara (right, facing three-quarter right, moving away from camera, right to left), Bolt (left)", asked);
        Assert.Contains("Mara faces the camera (three-quarter right) while going away from it", asked);
        Assert.Contains("frame before: rows e1–e2", asked);
        Assert.Contains("Mara moves right to left but travels left to right in this scene", asked);
        Assert.Contains("Mara and Bolt swap sides from the frame before, where Mara is left of Bolt", asked);
        var second = directed.Frames[1];
        Assert.Equal(("behind Bolt's shoulder", "Mara stops in front of Bolt."), (second.Camera, second.Beat));
        var maraSecond = second.Placements.Single(p => p.CharacterId == mara.Id);
        Assert.Equal(("left", "still"), (maraSecond.Screen, maraSecond.Moving));
        var boltSecond = second.Placements.Single(p => p.CharacterId == bolt.Id);
        Assert.Equal(("right", "screen left"), (boltSecond.Screen, boltSecond.Facing));
        Assert.Equal(3, _writer.ModelPrompts().Count);
    }

    [Fact]
    public async Task Directing_continuity_carries_the_previous_scenes_look_forward()
    {
        var (story, first, mara, bolt, radio, suit) = await DirectedSceneAsync();
        await _writer.SaveDirectionAsync(Direction(first, mara, bolt, radio, suit));
        const string text = "@Mara waits. @Bolt hums.";
        var second = await _writer.CreateSceneAsync(
            story.Containers[0].Id, 1, "Later", Heading(), Action(text, Mention(text, "@Mara", mara), Mention(text, "@Bolt", bolt)));
        // Mara keeps her look; Bolt gets one; the radio is not in this scene.
        _writer.ScriptModel("""
            {"looks":[{"name":"Mara","wardrobe":"work suit","appearanceChange":"dry now","changeReason":"The rain stopped.","changeLine":"e2"},{"name":"Bolt","present":false}],"props":[]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = second.Id, Continuity = true });

        var maraLook = Assert.Single(directed.Appearances, a => a.CharacterId == mara.Id);
        Assert.Equal(suit.Id, maraLook.WardrobeId);
        Assert.Equal(("dry now", "sleeves rolled"), (maraLook.AppearanceChange, maraLook.WardrobeChange));
        Assert.Equal(second.Elements[1].Id, maraLook.ChangeElementId);
        var boltLook = Assert.Single(directed.Appearances, a => a.CharacterId == bolt.Id);
        Assert.False(boltLook.HasWardrobeId);
        Assert.False(boltLook.OnScreen);
        Assert.Empty(directed.PropStates);
        Assert.Contains("previous look, as scene \"Untitled scene\" ended (day 2, a day not set; treat it as the same day): wardrobe \"work suit\", wardrobe change \"sleeves rolled\", appearance change \"wet hair\", holding []", _writer.ModelPrompts()[0]);
        Assert.Contains("This scene is on story day unknown", _writer.ModelPrompts()[0]);
    }

    [Fact]
    public async Task Directing_continuity_carries_what_someone_holds_into_the_next_scene_until_a_line_hands_it_over()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var bolt = await _writer.CreateCharacterAsync(story.Id, "Bolt", "Robot");
        var cooler = await _writer.CreatePropAsync(story.Id, "Cooler");
        const string arrival = "@Mara walks in, a @Cooler slung over her shoulder.";
        var first = await _writer.WriteFirstSceneAsync(story, Heading(),
            Action(arrival, Mention(arrival, "@Mara", mara), Mention(arrival, "@Cooler", cooler)), Dialogue(mara.Id, "Got a delivery."));
        _writer.ScriptModel("""
            {"looks":[{"name":"Mara","holding":["Cooler"],"changes":[]}],"props":[{"name":"Cooler","state":[]}]}
            """);
        var opened = await _writer.DirectAsync(new DirectRequest { SceneId = first.Id, Continuity = true });
        Assert.Equal([cooler.Id], Assert.Single(opened.Appearances).Holding);

        // The next scene never names the cooler; the answer forgets Mara holds it and has Bolt take it.
        const string snatch = "@Bolt snatches it and leaves.";
        var second = await _writer.CreateSceneAsync(story.Containers[0].Id, 1, "Hallway", Heading(),
            Action("@Mara waits.", Mention("@Mara waits.", "@Mara", mara)), Dialogue(bolt.Id, "Give."), Action(snatch, Mention(snatch, "@Bolt", bolt)));
        _writer.ScriptModel("""
            {"looks":[{"name":"Mara","holding":[],"changes":[]},{"name":"Bolt","holding":[],"changes":[{"line":"e4","takes":["Cooler"],"releases":[],"reason":"snatches the cooler"}]}],"props":[]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = second.Id, Continuity = true });

        Assert.Contains("holding [\"Cooler\"]", _writer.ModelPrompts()[^1]);
        Assert.Contains("- Cooler", _writer.ModelPrompts()[^1]);
        Assert.Equal([cooler.Id], directed.Appearances.Single(a => a.CharacterId == mara.Id).Holding);
        Assert.Contains(directed.PropStates, p => p.PropId == cooler.Id);
        var taken = Assert.Single(directed.LookChanges, c => c.CharacterId == bolt.Id);
        Assert.Equal((second.Elements[3].Id, "snatches the cooler"), (taken.ElementId, taken.Reason));
        Assert.Equal([cooler.Id], taken.Holding);
        // Taking it leaves Mara's hands empty on the same line.
        var released = Assert.Single(directed.LookChanges, c => c.CharacterId == mara.Id);
        Assert.Equal(second.Elements[3].Id, released.ElementId);
        Assert.Empty(released.Holding);
        Assert.Equal("lets go of the Cooler", released.Reason);
    }

    [Fact]
    public async Task A_look_change_round_trips_and_goes_with_its_line()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var direction = Direction(scene, mara, bolt, radio, suit);
        direction.Appearances[0].Holding.Add(radio.Id);
        direction.LookChanges.Add(new LookChange
        {
            CharacterId = mara.Id,
            ElementId = scene.Elements[3].Id,
            WardrobeId = suit.Id,
            AppearanceChange = "wet hair",
            Reason = "puts the radio down",
        });

        var saved = await _writer.SaveDirectionAsync(direction);

        Assert.Equal([radio.Id], saved.Appearances.Single().Holding);
        var change = Assert.Single(saved.LookChanges);
        Assert.Equal((scene.Elements[3].Id, suit.Id, "wet hair", "puts the radio down", true),
            (change.ElementId, change.WardrobeId, change.AppearanceChange, change.Reason, change.Edited));
        Assert.Empty(change.Holding);

        var sceneNow = await _writer.GetSceneAsync(scene.Id);
        sceneNow.Elements.RemoveAt(3);
        await _writer.SaveSceneAsync(sceneNow);
        Assert.Empty((await _writer.GetDirectionAsync(scene.Id)).LookChanges);
    }

    [Fact]
    public async Task Directing_continuity_keeps_the_previous_look_when_the_answer_gives_no_reason_to_change_it()
    {
        var (story, first, mara, bolt, radio, suit) = await DirectedSceneAsync();
        await _writer.SaveDirectionAsync(Direction(first, mara, bolt, radio, suit));
        const string text = "@Mara waits.";
        var second = await _writer.CreateSceneAsync(
            story.Containers[0].Id, 1, "Later", Heading(), Action(text, Mention(text, "@Mara", mara)));
        // The model names another wardrobe and an empty state but gives no change: the look carries forward.
        _writer.ScriptModel("""
            {"looks":[{"name":"Mara","wardrobe":"Sunday best","appearanceChange":"","changeReason":null,"changeLine":null}],"props":[]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = second.Id, Continuity = true });

        var look = Assert.Single(directed.Appearances);
        Assert.Equal(suit.Id, look.WardrobeId);
        Assert.Equal("wet hair", look.AppearanceChange);
        Assert.False(look.HasChangeReason);
    }

    [Fact]
    public async Task Directing_continuity_adds_a_wardrobe_the_text_describes_to_the_sheet()
    {
        var (story, first, mara, bolt, radio, suit) = await DirectedSceneAsync();
        await _writer.SaveDirectionAsync(Direction(first, mara, bolt, radio, suit));
        const string text = "@Mara comes down the stairs in a yellow oilskin.";
        var second = await _writer.CreateSceneAsync(
            story.Containers[0].Id, 1, "Later", Heading(), Action(text, Mention(text, "@Mara", mara)));
        _writer.ScriptModel("""
            {"looks":[{"name":"Mara","wardrobe":null,"newWardrobe":{"label":"yellow oilskin","description":"a yellow oilskin coat"},"appearanceChange":"","changeReason":"She has changed into the oilskin.","changeLine":"e2"}],"props":[]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = second.Id, Continuity = true });

        var character = (await _writer.GetStoryAsync(story.Id)).Characters.Single(c => c.Id == mara.Id);
        Assert.Equal(["work suit", "yellow oilskin"], character.Wardrobes.Select(w => w.Label));
        var look = Assert.Single(directed.Appearances);
        Assert.Equal(character.Wardrobes[1].Id, look.WardrobeId);
        Assert.Equal("She has changed into the oilskin.", look.ChangeReason);
        Assert.Equal(second.Elements[1].Id, look.ChangeElementId);
        Assert.Equal("", look.AppearanceChange);
    }

    [Fact]
    public async Task Directing_the_scene_brings_in_the_library_entries_the_text_implies()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var bolt = await _writer.CreateCharacterAsync(story.Id, "Bolt", "Robot");
        var radio = await _writer.CreatePropAsync(story.Id, "Radio");
        var docks = await _writer.CreateLocationAsync(story.Id, "Docks");
        await _writer.CreateLocationAsync(story.Id, "Office");
        const string text = "@Mara waits by the water. The robot hums, its radio crackling.";
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action(text, Mention(text, "@Mara", mara)));
        // Names outside the library, or already in the scene, are ignored.
        _writer.ScriptModel(
            """{"characters":["Bolt","Ghost","Mara"],"props":["Radio"],"locations":["Docks"]}""",
            """{"extras":[]}""",
            """{"storyDay":1}""",
            """{"looks":[{"name":"Mara"},{"name":"Bolt"}],"props":[{"name":"Radio","state":["crackling"]}],"locations":[{"name":"Docks","change":"fog on the water"}]}""",
            """{"frames":[{"from":"e1","to":"e2","shot":"wide","visible":"Mara and the robot at the water.","characters":[{"name":"Mara","screen":"left"},{"name":"Bolt","screen":"right"}],"props":["Radio"]}]}""",
            """{"frames":[{"frame":"f1","visible":"Mara stands at the edge of the Docks facing Bolt, who hums beside a stack of crates, its radio crackling."}]}""");

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Scene = true });

        Assert.Equal(new[] { mara.Id, bolt.Id }.Order(), directed.Appearances.Select(a => a.CharacterId).Order());
        Assert.Equal(["crackling"], Assert.Single(directed.PropStates, p => p.PropId == radio.Id).State);
        Assert.Equal("fog on the water", Assert.Single(directed.LocationStates, l => l.LocationId == docks.Id).Change);
        Assert.Equal("right", Assert.Single(directed.Frames).Placements.Single(p => p.CharacterId == bolt.Id).Screen);
        var presence = _writer.ModelPrompts()[0];
        Assert.Contains("Other characters in the library:\n- Bolt", presence);
        Assert.Contains("- Office", presence);
        Assert.Contains("- Bolt", _writer.ModelPrompts()[3]);
        // The frames pass left Bolt unnamed, so what the frame shows was written again.
        Assert.Contains("- Docks (exterior)", _writer.ModelPrompts()[4]);
        Assert.Contains("f1 rows e1–e2, wide shot; on screen: Mara (left), Bolt (right); props in view: Radio", _writer.ModelPrompts()[5]);
        Assert.StartsWith("Mara stands at the edge of the Docks facing Bolt", Assert.Single(directed.Frames).Beat);
    }

    [Fact]
    public async Task Directing_frames_names_everyone_on_screen_even_when_the_rewrite_leaves_someone_out()
    {
        var (_, scene, mara, bolt, radio, suit) = await DirectedSceneAsync();
        var direction = Direction(scene, mara, bolt, radio, suit);
        direction.Frames.Clear();
        await _writer.SaveDirectionAsync(direction);
        _writer.ScriptModel(
            """{"extras":[]}""",
            """{"frames":[{"from":"e1","to":"e5","shot":"medium","visible":"","characters":[{"name":"Mara","screen":"left","pose":"leaning on the rail","expression":"wary"},{"name":"Bolt","screen":"right"}]}]}""",
            """{"frames":[{"frame":"f1","visible":"Mara leans on the rail of the pier, watching the water."}]}""");

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, Frames = true });

        Assert.Equal(
            "Mara leans on the rail of the pier, watching the water. Bolt is screen right.",
            Assert.Single(directed.Frames).Beat);
    }

    [Fact]
    public async Task Describing_a_character_writes_the_sheet_and_keeps_existing_wardrobes()
    {
        var (story, _, mara, _, _, suit) = await DirectedSceneAsync();
        _writer.ScriptModel("""
            {"kind":"Human","description":"Mara runs the night shift at the docks.","identity":{"age":"late 30s","gender":"female","build":"wiry","face":"sharp","hair":"cropped black","skin":"olive","marks":["scar over left brow"]},"voice":{"description":"low, quick, Glaswegian","baseline":"dry"},"wardrobes":[{"label":"Work Suit","description":"newer overalls"},{"label":"rain coat","description":"yellow oilskin"}]}
            """, """{"voice":"v2"}""");

        var reply = await _writer.Direct((d, c) => d.Describe(new DescribeRequest { CharacterId = mara.Id }, c));

        var character = reply.Character;
        Assert.Equal("Mara runs the night shift at the docks.", character.Description);
        Assert.Equal("late 30s", character.Appearance.Age);
        Assert.Equal(["scar over left brow"], character.Appearance.Marks);
        Assert.Equal("dry", character.Voice.Baseline);
        Assert.Equal(["work suit", "rain coat"], character.Wardrobes.Select(w => w.Label));
        Assert.Equal(suit.Id, character.Wardrobes[0].Id);
        Assert.Contains("they say: Don't move.", _writer.ModelPrompts()[0]);
        Assert.Equal(character, (await _writer.GetStoryAsync(story.Id)).Characters.Single(c => c.Id == mara.Id));
    }

    [Fact]
    public async Task Describing_casts_a_voice_from_the_sheet_and_casting_again_picks_another()
    {
        var (_, _, mara, _, _, _) = await DirectedSceneAsync();
        _writer.ScriptModel("""
            {"kind":"Human","description":"Mara runs the night shift.","identity":{"age":"late 30s","gender":"Female","build":"","face":"","hair":"","skin":"","marks":[]},"voice":{"description":"low, quick, Glaswegian","baseline":"dry"},"wardrobes":[]}
            """, """{"voice":"v2"}""");

        var described = (await _writer.Direct((d, c) => d.Describe(new DescribeRequest { CharacterId = mara.Id }, c))).Character;

        Assert.Equal("Human", described.Kind);
        Assert.Equal(("c4", "Morag", "https://example.test/morag.mp3"), (described.Voice.VoiceId, described.Voice.VoiceName, described.Voice.PreviewUrl));
        Assert.Equal("low, quick, Glaswegian", described.Voice.Description);
        Assert.Contains("v1: Isla (female · middle aged · scottish)", _writer.ModelPrompts()[1]);

        var recast = (await _writer.Direct((d, c) => d.CastVoice(new CastVoiceRequest { CharacterId = mara.Id }, c))).Character;

        Assert.Equal(("c1", "Isla"), (recast.Voice.VoiceId, recast.Voice.VoiceName));
        Assert.Equal("dry", recast.Voice.Baseline);
        Assert.Equal(2, _writer.ModelPrompts().Count);
    }

    [Fact]
    public async Task An_answer_the_director_cannot_use_is_reported_in_words_after_one_retry()
    {
        var (_, scene, _, _, _, _) = await DirectedSceneAsync();
        _writer.ScriptModel("I'd rather not.", "Still no.");

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, StoryDay = true }));

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        Assert.Equal(DirectionErrors.ModelUnusable, error.Status.Detail);
        Assert.Equal(2, _writer.ModelPrompts().Count);
        Assert.False((await _writer.GetDirectionAsync(scene.Id)).HasStoryDay);
    }

    [Fact]
    public async Task Another_users_scene_reads_as_not_found()
    {
        var (_, scene, _, _, _, _) = await DirectedSceneAsync();
        var stranger = new Writer(database);

        var error = await Assert.ThrowsAsync<RpcException>(() => stranger.GetDirectionAsync(scene.Id));

        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }
}
