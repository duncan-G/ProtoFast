using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Services.Imaging;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class ImageTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    private ScriptedImageGenerator Images => database.Services.GetRequiredService<ScriptedImageGenerator>();

    private MemoryObjectStore Store => database.Services.GetRequiredService<MemoryObjectStore>();

    /// <summary>Mara at the Harbour across three frames; the radio is in view throughout.</summary>
    private async Task<(Story Story, Scene Scene, Character Mara, Location Harbour, Prop Radio)> FramedSceneAsync()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var harbour = await _writer.CreateLocationAsync(story.Id, "Harbour");
        var radio = await _writer.CreatePropAsync(story.Id, "Radio");
        await _writer.Call((s, c) => s.UpdateCharacterSheet(new UpdateCharacterSheetRequest
        {
            CharacterId = mara.Id,
            Appearance = new CharacterIdentity { Age = "forties", Hair = "grey crop" },
            Wardrobes = { new Wardrobe { Label = "Work suit", Description = "orange salvage suit" } },
        }, c));
        var action = "@Mara tunes the @Radio.";
        var scene = await _writer.WriteFirstSceneAsync(
            story,
            Heading(harbour.Id, "NIGHT"),
            Action(action, Mention(action, "@Mara", mara), Mention(action, "@Radio", radio)),
            Dialogue(mara.Id, "Anyone out there?"),
            Action("Static answers."));
        var direction = new SceneDirection { SceneId = scene.Id, Shot = "medium" };
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[0].Id, LastElementId = scene.Elements[1].Id, Beat = "Mara at the radio." });
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[2].Id, LastElementId = scene.Elements[2].Id, Beat = "Mara calls out." });
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[3].Id, LastElementId = scene.Elements[3].Id, Beat = "Mara listens." });
        await _writer.SaveDirectionAsync(direction);
        return (story, scene, mara, harbour, radio);
    }

    private Task<GenerateFrameImageReply> PictureAsync(Scene scene, SceneDirection direction, int frame, bool regenerate = false) =>
        _writer.Direct((d, c) => d.GenerateFrameImage(new GenerateFrameImageRequest
        {
            SceneId = scene.Id,
            FrameId = direction.Frames[frame].Id,
            Regenerate = regenerate,
        }, c));

    private Task<GenerateLibraryImageReply> PictureCharacterAsync(Character character, bool regenerate = false) =>
        _writer.Direct((d, c) => d.GenerateLibraryImage(new GenerateLibraryImageRequest { CharacterId = character.Id, Regenerate = regenerate }, c));

    [Fact]
    public async Task A_library_entry_is_pictured_from_its_sheet_once_and_reads_stale_when_the_sheet_changes()
    {
        var (story, _, mara, _, _) = await FramedSceneAsync();
        var before = Images.Requests.Count;

        var first = await PictureCharacterAsync(mara);

        Assert.Equal("character", first.Image.SubjectKind);
        Assert.False(first.Image.Image.Stale);
        Assert.StartsWith($"https://media.test/stories/{story.Id}/images/", first.Image.Image.Url);
        Assert.True(Store.Objects.ContainsKey(first.Image.Image.Url.Replace("https://media.test/", "")));
        var request = Images.Requests[^1];
        Assert.Contains("reference sheet of Mara", request.Prompt);
        Assert.Contains("Hair: grey crop.", request.Prompt);
        Assert.Contains("Work suit: orange salvage suit", request.Prompt);
        Assert.Empty(request.References);

        // Current: asking again draws nothing.
        await PictureCharacterAsync(mara);
        Assert.Equal(1, Images.Requests.Count - before);

        await _writer.Call((s, c) => s.UpdateCharacterSheet(new UpdateCharacterSheetRequest
        {
            CharacterId = mara.Id,
            Appearance = new CharacterIdentity { Age = "forties", Hair = "shaved" },
            Wardrobes = { new Wardrobe { Label = "Work suit", Description = "orange salvage suit" } },
        }, c));
        var listed = await _writer.Direct((d, c) => d.ListLibraryImages(new ListLibraryImagesRequest { StoryId = story.Id }, c));
        Assert.True(Assert.Single(listed.Images).Image.Stale);

        var again = await PictureCharacterAsync(mara);
        Assert.False(again.Image.Image.Stale);
        Assert.Equal(2, Images.Requests.Count - before);
    }

    [Fact]
    public async Task A_middle_frame_is_composed_from_what_exists_without_picturing_its_neighbours()
    {
        var (_, scene, mara, _, _) = await FramedSceneAsync();
        await PictureCharacterAsync(mara);
        var direction = await _writer.GetDirectionAsync(scene.Id);
        var before = Images.Requests.Count;

        var reply = await PictureAsync(scene, direction, 1);

        Assert.Equal(1, Images.Requests.Count - before);
        Assert.Null(reply.Direction.Frames[0].Image);
        Assert.Null(reply.Direction.Frames[2].Image);
        var middle = reply.Direction.Frames[1].Image;
        Assert.False(middle.Stale);
        Assert.Equal(["Mara"], middle.References);
        Assert.Equal("Drew it from 1 references.", middle.Notes);

        var request = Images.Requests[^1];
        Assert.Equal(ImagePrompts.ReferencesPreamble, request.Preamble);
        Assert.StartsWith("Image 1: The library's reference sheet of Mara", Assert.Single(request.References).Caption);
        Assert.Contains("Place: Harbour, an exterior.", request.Prompt);
        Assert.Contains("Time of day: night.", request.Prompt);
        Assert.Contains("Camera: medium shot.", request.Prompt);
        Assert.Contains("In the picture: Mara calls out.", request.Prompt);
        Assert.Contains("Radio", request.Prompt);

        // The next frame leads with the one before it.
        var next = await PictureAsync(scene, reply.Direction, 2);
        Assert.Equal(["Frame 2", "Mara"], next.Direction.Frames[2].Image.References);
        Assert.StartsWith("Image 1: Frame 2 of this scene, the shot just before this one, showing Mara at Harbour (night), medium shot: Mara calls out. This picture continues from it", Images.Requests[^1].References[0].Caption);

        // Current frames draw nothing again.
        await PictureAsync(scene, next.Direction, 1);
        Assert.Equal(2, Images.Requests.Count - before);
    }

    [Fact]
    public async Task A_locked_frame_is_never_pictured()
    {
        var (_, scene, _, _, _) = await FramedSceneAsync();
        var direction = await _writer.GetDirectionAsync(scene.Id);
        direction.Frames[1].Locked = true;
        direction = await _writer.SaveDirectionAsync(direction);
        var before = Images.Requests.Count;

        var reply = await PictureAsync(scene, direction, 1, regenerate: true);

        Assert.Equal(before, Images.Requests.Count);
        Assert.Null(reply.Direction.Frames[1].Image);
    }

    [Fact]
    public async Task A_variation_is_the_previous_frames_picture_edited_and_follows_it()
    {
        var (_, scene, mara, _, _) = await FramedSceneAsync();
        await PictureCharacterAsync(mara);
        var direction = await _writer.GetDirectionAsync(scene.Id);
        direction.Frames[1].Variation = "Mara lifts the radio to her mouth.";
        direction = await _writer.SaveDirectionAsync(direction);

        // With nothing to edit yet, it is pictured from its full description.
        direction = (await PictureAsync(scene, direction, 1)).Direction;
        Assert.Contains("Place: Harbour", Images.Requests[^1].Prompt);

        direction = (await PictureAsync(scene, direction, 0)).Direction;
        Assert.True(direction.Frames[1].Image.Stale);

        var before = Images.Requests.Count;
        direction = (await PictureAsync(scene, direction, 1)).Direction;
        var request = Images.Requests[^1];
        Assert.Equal(1, Images.Requests.Count - before);
        Assert.Equal("", request.Preamble);
        Assert.Equal("Frame 1 of this scene: the picture to edit.", Assert.Single(request.References).Caption);
        Assert.StartsWith("Edit this picture: Mara lifts the radio to her mouth.\nChange nothing else.", request.Prompt);
        Assert.DoesNotContain("Harbour", request.Prompt);
        Assert.Equal(["Frame 1"], direction.Frames[1].Image.References);
        Assert.False(direction.Frames[1].Image.Stale);

        // A new picture of the frame it edits stales it, and so does a new change.
        direction = (await PictureAsync(scene, direction, 0, regenerate: true)).Direction;
        Assert.True(direction.Frames[1].Image.Stale);
        direction = (await PictureAsync(scene, direction, 1)).Direction;
        direction.Frames[1].Variation = "Mara sets the radio down.";
        Assert.True((await _writer.SaveDirectionAsync(direction)).Frames[1].Image.Stale);
    }

    [Fact]
    public async Task A_frame_reads_stale_when_what_it_shows_or_a_library_picture_it_used_changes()
    {
        var (_, scene, mara, _, _) = await FramedSceneAsync();
        await PictureCharacterAsync(mara);
        var direction = (await PictureAsync(scene, await _writer.GetDirectionAsync(scene.Id), 0)).Direction;
        direction = (await PictureAsync(scene, direction, 1)).Direction;
        Assert.All(direction.Frames.Take(2), f => Assert.False(f.Image.Stale));

        // A new beat changes what frame 1 shows.
        direction.Frames[0].Beat = "Mara slams the radio.";
        var saved = await _writer.SaveDirectionAsync(direction);
        Assert.True(saved.Frames[0].Image.Stale);
        Assert.False(saved.Frames[1].Image.Stale);

        // Redrawing Mara's library picture stales every frame composed from it.
        await PictureCharacterAsync(mara, regenerate: true);
        var after = await _writer.GetDirectionAsync(scene.Id);
        Assert.True(after.Frames[1].Image.Stale);

        // Generating a stale frame draws it again, in place.
        var before = Images.Requests.Count;
        var redrawn = await PictureAsync(scene, after, 1);
        Assert.Equal(1, Images.Requests.Count - before);
        Assert.False(redrawn.Direction.Frames[1].Image.Stale);
        Assert.Equal(direction.Frames[1].Image.AssetId, redrawn.Direction.Frames[1].Image.AssetId);
    }

    [Fact]
    public async Task A_frame_is_offered_the_latest_picture_of_its_place_from_an_earlier_scene()
    {
        var (story, scene, mara, harbour, _) = await FramedSceneAsync();
        var direction = (await PictureAsync(scene, await _writer.GetDirectionAsync(scene.Id), 2)).Direction;
        var full = await _writer.GetStoryAsync(story.Id);
        var later = await _writer.CreateSceneAsync(full.Containers[0].Id, 1, "Back at the harbour",
            Heading(harbour.Id, "DAY"), Action("Gulls over empty water."));
        var laterDirection = new SceneDirection { SceneId = later.Id };
        laterDirection.Frames.Add(new Frame { FirstElementId = later.Elements[0].Id, LastElementId = later.Elements[1].Id, Beat = "The water." });
        laterDirection = await _writer.SaveDirectionAsync(laterDirection);

        var reply = await PictureAsync(later, laterDirection, 0);

        var earlier = $"Frame 3 of {(string.IsNullOrWhiteSpace(scene.Title) ? "scene 1" : scene.Title.Trim())}";
        Assert.Equal([earlier], reply.Direction.Frames[0].Image.References);
        Assert.Contains("of an earlier scene", Images.Requests[^1].References[0].Caption);
        Assert.NotNull(direction.Frames[2].Image);
    }

    [Fact]
    public async Task A_character_carrying_a_prop_is_drawn_from_their_look_picture_until_they_put_it_down()
    {
        var (story, scene, mara, _, radio) = await FramedSceneAsync();
        var direction = await _writer.GetDirectionAsync(scene.Id);
        direction.Appearances.Add(new SceneAppearance { CharacterId = mara.Id, Holding = { radio.Id } });
        direction.LookChanges.Add(new LookChange { CharacterId = mara.Id, ElementId = scene.Elements[3].Id, Reason = "puts the radio down" });
        direction = await _writer.SaveDirectionAsync(direction);
        var before = Images.Requests.Count;

        var first = await PictureAsync(scene, direction, 0);

        // The library sheet first, then the look composed from it, then the frame.
        Assert.Equal(3, Images.Requests.Count - before);
        var look = Images.Requests[^2];
        Assert.Contains("as they look at this point of the story", look.Prompt);
        Assert.Contains("Carrying, the same way in both views: Radio.", look.Prompt);
        Assert.StartsWith("Image 1: The library's reference sheet of Mara in their usual look", Assert.Single(look.References).Caption);
        var frame = Images.Requests[^1];
        Assert.Contains("Carrying: Radio.", frame.Prompt);
        Assert.StartsWith("Image 1: The reference sheet of Mara in the look they have in this frame, carrying Radio.", frame.References[0].Caption);
        Assert.Equal(["Mara (carrying Radio)"], first.Direction.Frames[0].Image.References);
        var pictured = first.Direction.Appearances.Single().Image;
        Assert.False(pictured.Stale);

        // The next frame in the same look draws only itself.
        var second = await PictureAsync(scene, first.Direction, 1);
        Assert.Equal(4, Images.Requests.Count - before);
        Assert.Equal(["Frame 1", "Mara (carrying Radio)"], second.Direction.Frames[1].Image.References);

        // Once she puts it down she is drawn from the library's sheet again, and the radio stands alone.
        var third = await PictureAsync(scene, second.Direction, 2);
        Assert.Equal(5, Images.Requests.Count - before);
        Assert.Equal("Mara", third.Direction.Frames[2].Image.References[1]);
        Assert.DoesNotContain("Carrying", Images.Requests[^1].Prompt);
        Assert.Null(Assert.Single(third.Direction.LookChanges).Image);

        // A later scene that opens in the same look reuses the picture.
        var full = await _writer.GetStoryAsync(story.Id);
        const string text = "@Mara walks the pier with the @Radio.";
        var later = await _writer.CreateSceneAsync(full.Containers[0].Id, 1, "Pier",
            Heading(), Action(text, Mention(text, "@Mara", mara), Mention(text, "@Radio", radio)));
        var laterDirection = new SceneDirection { SceneId = later.Id };
        laterDirection.Appearances.Add(new SceneAppearance { CharacterId = mara.Id, Holding = { radio.Id } });
        laterDirection.Frames.Add(new Frame { FirstElementId = later.Elements[0].Id, LastElementId = later.Elements[1].Id, Beat = "Mara on the pier." });
        laterDirection = await _writer.SaveDirectionAsync(laterDirection);
        var pier = await PictureAsync(later, laterDirection, 0);
        Assert.Equal(6, Images.Requests.Count - before);
        Assert.Equal(pictured.AssetId, pier.Direction.Appearances.Single().Image.AssetId);
    }

    [Fact]
    public async Task A_frame_picture_is_told_the_camera_and_which_way_everyone_faces_moves_and_looks()
    {
        var (story, scene, mara, harbour, _) = await FramedSceneAsync();
        var crowd = await _writer.CreateExtraAsync(story.Id, "Dockhands", "Three dockhands.", "hi-vis vests");
        await _writer.Call((s, c) => s.UpdateLocationSheet(new UpdateLocationSheetRequest
        {
            LocationId = harbour.Id,
            Appearance = new LocationAppearance { Layout = "A gate at the landward end; the quay runs out to sea, cranes on the left." },
        }, c));
        var direction = await _writer.GetDirectionAsync(scene.Id);
        direction.Extras.Add(new SceneExtra { ExtraId = crowd.Id });
        direction.Frames[1].Camera = "on the quay, looking back at the gate.";
        direction.Frames[1].Placements.Add(new Placement
        {
            CharacterId = mara.Id,
            Screen = "left",
            Facing = "three-quarter right",
            Moving = "toward camera, left to right",
            Looking = "the Dockhands",
            Pose = "a palm on the gate",
        });
        direction.Frames[1].Extras.Add(new FrameExtra { ExtraId = crowd.Id, Screen = "right", Facing = "back three-quarter left", Pose = "the nearest one, mid-step" });
        var saved = await _writer.SaveDirectionAsync(direction);

        await PictureAsync(scene, saved, 1);

        var prompt = Images.Requests[^1].Prompt;
        Assert.Contains("How the place is laid out (a map of it, not this picture's camera): A gate at the landward end", prompt);
        Assert.Contains(
            "Staging: frame left; face toward the camera, body turned three-quarters toward screen right; coming toward the camera, moving from screen left toward screen right; looking at the Dockhands; a palm on the gate.",
            prompt);
        Assert.Contains("Wearing: hi-vis vests\nStaging: frame right; back to the camera, body turned three-quarters toward screen left; the nearest one, mid-step.", prompt);
        Assert.Contains("Camera: medium shot, on the quay, looking back at the gate.", prompt);
        Assert.Contains("Left and right mean the viewer's left and right in the picture, never a person's own.", prompt);
    }

    [Fact]
    public async Task A_frame_pictures_its_extras_from_their_library_picture()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var crowd = await _writer.CreateExtraAsync(story.Id, "Crowd", "Forty commuters.", "winter coats");
        const string text = "@Mara waits on the platform.";
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action(text, Mention(text, "@Mara", mara)));
        var direction = new SceneDirection { SceneId = scene.Id };
        direction.Extras.Add(new SceneExtra { ExtraId = crowd.Id });
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[0].Id, LastElementId = scene.Elements[1].Id, Beat = "Mara among the Crowd." });
        var saved = await _writer.SaveDirectionAsync(direction);

        var library = await _writer.Direct((d, c) => d.GenerateLibraryImage(new GenerateLibraryImageRequest { ExtraId = crowd.Id }, c));
        Assert.Equal(("extra", crowd.Id), (library.Image.SubjectKind, library.Image.SubjectId));
        Assert.Contains("A reference picture of Crowd", Images.Requests[^1].Prompt);
        Assert.Contains("Wearing: winter coats", Images.Requests[^1].Prompt);

        var pictured = await _writer.Direct((d, c) => d.GenerateFrameImage(
            new GenerateFrameImageRequest { SceneId = scene.Id, FrameId = saved.Frames[0].Id }, c));

        Assert.Equal(["Crowd"], pictured.Direction.Frames[0].Image.References);
        var request = Images.Requests[^1];
        Assert.Contains("Crowd (extras, in the background):\nForty commuters.\nWearing: winter coats", request.Prompt);
        Assert.Contains("Exactly one person in the picture besides the extras described.", request.Prompt);

        // Changing the extras' sheet stales their picture and the frame composed from it.
        await _writer.Call((s, c) => s.UpdateExtra(new UpdateExtraRequest { ExtraId = crowd.Id, Name = "Crowd", Description = "Ten commuters." }, c));
        var images = await _writer.Direct((d, c) => d.ListLibraryImages(new ListLibraryImagesRequest { StoryId = story.Id }, c));
        Assert.True(Assert.Single(images.Images).Image.Stale);
        Assert.True((await _writer.GetDirectionAsync(scene.Id)).Frames[0].Image.Stale);
    }

    [Fact]
    public async Task One_extra_is_pictured_alone_and_a_big_crowd_as_a_line_up()
    {
        var story = await _writer.CreateStoryAsync();
        var nurse = await _writer.CreateExtraAsync(story.Id, "Trauma nurse", "In her twenties, holding a clipboard.", "scrubs", headcount: 1);
        var crowd = await _writer.CreateExtraAsync(story.Id, "Commuters", "Office workers.", headcount: 40);
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        const string text = "@Mara waits by the desk.";
        var scene = await _writer.WriteFirstSceneAsync(story, Heading(), Action(text, Mention(text, "@Mara", mara)));
        var direction = new SceneDirection { SceneId = scene.Id };
        direction.Extras.Add(new SceneExtra { ExtraId = nurse.Id });
        direction.Frames.Add(new Frame { FirstElementId = scene.Elements[0].Id, LastElementId = scene.Elements[1].Id, Beat = "Mara by the desk." });
        var saved = await _writer.SaveDirectionAsync(direction);

        await _writer.Direct((d, c) => d.GenerateLibraryImage(new GenerateLibraryImageRequest { ExtraId = nurse.Id }, c));
        var alone = Images.Requests[^1].Prompt;
        Assert.Contains("one uncast person in the background of this production: that one person alone", alone);
        Assert.DoesNotContain("side by side", alone);

        await _writer.Direct((d, c) => d.GenerateLibraryImage(new GenerateLibraryImageRequest { ExtraId = crowd.Id }, c));
        Assert.Contains("a crowd of 40 uncast people in the background of this production: a line-up of 8 of them", Images.Requests[^1].Prompt);

        await _writer.Direct((d, c) => d.GenerateFrameImage(new GenerateFrameImageRequest { SceneId = scene.Id, FrameId = saved.Frames[0].Id }, c));
        Assert.Contains("Trauma nurse (extras, in the background, one person):", Images.Requests[^1].Prompt);
    }
}
