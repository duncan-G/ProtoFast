using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.Api.Services.Directing;
using ProtoFast.Database.Abstractions;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class FrameStagingTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    private ScriptedLanguageModelFactory Model => database.Services.GetRequiredService<ScriptedLanguageModelFactory>();

    /// <summary>Mara at the Harbour tuning the radio, across three frames.</summary>
    private async Task<(Scene Scene, SceneDirection Direction, Character Mara, Location Harbour, Prop Radio)> FramedSceneAsync()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var harbour = await _writer.CreateLocationAsync(story.Id, "Harbour");
        var radio = await _writer.CreatePropAsync(story.Id, "Radio");
        var action = "@Mara tunes the @Radio.";
        var scene = await _writer.WriteFirstSceneAsync(
            story,
            Heading(harbour.Id, "NIGHT"),
            Action(action, Mention(action, "@Mara", mara), Mention(action, "@Radio", radio)),
            Dialogue(mara.Id, "Anyone out there?"),
            Action("Static answers."));
        var direction = new SceneDirection { SceneId = scene.Id, Shot = "medium" };
        direction.Frames.Add(new Frame
        {
            FirstElementId = scene.Elements[0].Id,
            LastElementId = scene.Elements[1].Id,
            Beat = "Mara at the radio.",
            Placements = { new Placement { CharacterId = mara.Id, Screen = "left", Depth = "midground", Mark = "at the radio table" } },
            Props = { new FrameProp { PropId = radio.Id, InView = true, Screen = "left", Depth = "midground", Mark = "on the radio table" } },
        });
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[2].Id, LastElementId = scene.Elements[2].Id, Beat = "Mara calls out." });
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[3].Id, LastElementId = scene.Elements[3].Id, Beat = "Mara listens." });
        return (scene, await _writer.SaveDirectionAsync(direction), mara, harbour, radio);
    }

    /// <summary>The director with checks on, run over the scene as the Direct call would, returning its draft unsaved.</summary>
    private async Task<DirectionDraft> DirectVerifiedAsync(DirectRequest request)
    {
        var options = new DirectorOptions { Verify = true };
        var director = new SceneDirector(
            Model, options, new FrameVerifier(Model, options, NullLogger<FrameVerifier>.Instance),
            database.Services.GetRequiredService<PictureReader>(), NullLogger<SceneDirector>.Instance);
        await using var scope = database.Services.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(_writer.UserId);
        ScriptedLanguageModelFactory.User.Value = _writer.UserId;
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadOnly(nameof(DirectVerifiedAsync));
        var directed = await scope.ServiceProvider.GetRequiredService<DirectionScope>()
            .LoadAsync(Guid.Parse(request.SceneId), withEarlierScenes: true, TestContext.Current.CancellationToken);
        return await director.DirectAsync(directed, request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Suggesting_a_frame_shows_the_place_and_the_frame_before_and_stores_where_everyone_and_everything_is()
    {
        var (scene, direction, mara, harbour, radio) = await FramedSceneAsync();
        await _writer.Direct((d, c) => d.GenerateLibraryImage(new GenerateLibraryImageRequest { LocationId = harbour.Id }, c));
        await _writer.Direct((d, c) => d.GenerateFrameImage(new GenerateFrameImageRequest { SceneId = scene.Id, FrameId = direction.Frames[0].Id }, c));
        _writer.ScriptModel("""{"extras":[]}""", """
            {"shot":"close","camera":"across the radio table from her","visible":"Mara leans over the radio, mouth open mid-call.",
             "characters":[{"name":"Mara","mark":"at the radio table","screen":"left","depth":"Mid ground","facing":"three-quarter right","moving":"still","pose":"leaning in"}],
             "props":[{"name":"Radio","mark":"on the radio table","screen":"centre","depth":"foreground"}]}
            """);

        var directed = await _writer.DirectAsync(new DirectRequest { SceneId = scene.Id, FrameId = direction.Frames[1].Id });

        var asked = _writer.ModelPrompts()[^1];
        Assert.Contains("This frame follows f1: start from where f1 has everyone and everything", asked);
        Assert.Contains("f1 rows e1–e2, scene shot; on screen: Mara (left, midground, at the radio table); props in view: Radio (left, midground, on the radio table)", asked);
        Assert.Contains("f2 (this frame, as staged now)", asked);
        var shown = Model.Pictures(_writer.UserId)[^1];
        Assert.Equal(2, shown.Count);
        Assert.StartsWith("Harbour, its establishing view", shown[0]);
        Assert.StartsWith("The frame before this one (f1)", shown[1]);

        var placement = Assert.Single(directed.Frames[1].Placements, p => p.CharacterId == mara.Id);
        Assert.Equal(("left", "midground", "at the radio table"), (placement.Screen, placement.Depth, placement.Mark));
        var prop = Assert.Single(directed.Frames[1].Props);
        Assert.Equal((radio.Id, true, "centre", "foreground", "on the radio table"), (prop.PropId, prop.InView, prop.Screen, prop.Depth, prop.Mark));
    }

    [Fact]
    public async Task A_frame_is_pictured_with_everyone_and_everything_where_it_puts_them()
    {
        var (scene, direction, _, _, _) = await FramedSceneAsync();

        await _writer.Direct((d, c) => d.GenerateFrameImage(new GenerateFrameImageRequest { SceneId = scene.Id, FrameId = direction.Frames[0].Id }, c));

        var prompt = database.Services.GetRequiredService<ScriptedImageGenerator>().Requests.Last(r => r.Prompt.Contains("Mara at the radio.")).Prompt;
        Assert.Contains("Staging: frame left, in the midground, at the radio table.", prompt);
        Assert.Contains("Radio\nWhere it is: frame left, in the midground, on the radio table.", prompt);
    }

    [Fact]
    public async Task A_suggested_frame_is_checked_fixed_once_and_what_is_still_wrong_is_reported()
    {
        var (scene, direction, mara, _, _) = await FramedSceneAsync();
        _writer.ScriptModel(
            """{"extras":[]}""",
            """{"shot":"medium","visible":"Mara calls into the radio.","characters":[{"name":"Mara","screen":"left","depth":"midground","facing":"three-quarter right","moving":"still"}],"props":["Radio"]}""",
            """{"findings":[]}""",
            """{"findings":[{"frame":"f2","issue":"Mara is turned away from the radio she calls into; turn her toward it."}]}""",
            """
            {"frames":[{"frame":"f2","camera":"across the radio table","visible":"Mara bends to the radio, calling into it.",
             "characters":[{"name":"Mara","mark":"at the radio table","screen":"left","depth":"midground","facing":"three-quarter right","moving":"still","looking":"Radio"}],
             "props":[{"name":"Radio","mark":"on the radio table","screen":"left","depth":"midground"}]}]}
            """,
            """{"findings":[{"frame":"f2","issue":"The radio's dial glows here but was dark in f1."}]}""",
            """{"findings":[]}""");

        var draft = await DirectVerifiedAsync(new DirectRequest { SceneId = scene.Id, FrameId = direction.Frames[1].Id });

        var prompts = _writer.ModelPrompts();
        var fix = Assert.Single(prompts, p => p.StartsWith("Checks found problems"));
        Assert.Contains("f2 (fix) rows e3–e3", fix);
        Assert.Contains("positioning: Mara is on screen with no mark: say where in the place and the picture they are", fix);
        Assert.Contains("positioning: Mara is turned away from the radio she calls into", fix);
        Assert.Contains("positioning: Radio is in view with no mark", fix);

        var frame = draft.Frames.Single(f => f.Id == Guid.Parse(direction.Frames[1].Id));
        Assert.Equal(("across the radio table", "at the radio table"), (frame.Camera, frame.Placements.Single(p => p.CharacterId == Guid.Parse(mara.Id)).Mark));
        Assert.Equal("on the radio table", Assert.Single(frame.Props).Mark);
        var finding = Assert.Single(draft.Findings);
        Assert.Equal((frame, FrameChecks.Consistency, "The radio's dial glows here but was dark in f1."), (finding.Frame, finding.Check, finding.Issue));
    }

    [Fact]
    public async Task A_frame_with_everyone_placed_and_nothing_found_is_not_asked_again()
    {
        var (scene, direction, _, _, _) = await FramedSceneAsync();
        _writer.ScriptModel(
            """{"extras":[]}""",
            """{"shot":"medium","visible":"Mara calls into the radio.","characters":[{"name":"Mara","mark":"at the radio table","screen":"left","depth":"midground","facing":"three-quarter right","moving":"still"}],"props":[{"name":"Radio","mark":"on the radio table","screen":"left","depth":"midground"}]}""",
            """{"findings":[]}""",
            """{"findings":[]}""");

        var draft = await DirectVerifiedAsync(new DirectRequest { SceneId = scene.Id, FrameId = direction.Frames[1].Id });

        Assert.Empty(draft.Findings);
        Assert.Equal(4, _writer.ModelPrompts().Count);
        Assert.StartsWith("Check each frame marked (check) for consistency", _writer.ModelPrompts()[2]);
        Assert.StartsWith("Check each frame marked (check) for positioning", _writer.ModelPrompts()[3]);
    }

    [Fact]
    public async Task A_verifier_that_fails_leaves_the_frame_as_suggested()
    {
        var (scene, direction, _, _, _) = await FramedSceneAsync();
        _writer.ScriptModel(
            """{"extras":[]}""",
            """{"shot":"medium","visible":"Mara calls into the radio.","characters":[{"name":"Mara","mark":"at the radio table","screen":"left","depth":"midground"}],"props":[{"name":"Radio","mark":"on the radio table","screen":"left","depth":"midground"}]}""",
            "Looks fine to me.",
            "Still fine.",
            """{"findings":[]}""");

        var draft = await DirectVerifiedAsync(new DirectRequest { SceneId = scene.Id, FrameId = direction.Frames[1].Id });

        Assert.Empty(draft.Findings);
        Assert.Equal("Mara calls into the radio.", draft.Frames.Single(f => f.Id == Guid.Parse(direction.Frames[1].Id)).Beat);
    }
}
