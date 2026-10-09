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
        Assert.Contains("Action: Mara calls out.", request.Prompt);
        Assert.Contains("Radio", request.Prompt);

        // The next frame is offered the one before it as well.
        var next = await PictureAsync(scene, reply.Direction, 2);
        Assert.Equal(["Mara", "Frame 2"], next.Direction.Frames[2].Image.References);
        Assert.Equal("Image 2: Frame 2 of this scene (1 before this one), showing Mara at Harbour (night), medium shot: Mara calls out.", Images.Requests[^1].References[1].Caption);

        // Current frames draw nothing again.
        await PictureAsync(scene, next.Direction, 1);
        Assert.Equal(2, Images.Requests.Count - before);
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
}
