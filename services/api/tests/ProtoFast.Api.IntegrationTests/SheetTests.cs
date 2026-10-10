using Grpc.Core;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class SheetTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    [Fact]
    public async Task A_character_sheet_round_trips_and_a_relabelled_wardrobe_keeps_its_id()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var first = await _writer.Call((s, c) => s.UpdateCharacterSheet(new UpdateCharacterSheetRequest
        {
            CharacterId = mara.Id,
            Description = "  A dock worker.  ",
            Appearance = new CharacterIdentity { Age = "30s", Build = "wiry", Face = "sharp", Hair = "cropped", Skin = "olive", Marks = { "scar" } },
            Voice = new VoiceProfile { Description = "low", Baseline = "dry" },
            Wardrobes = { new Wardrobe { Label = "work suit", Description = "overalls" }, new Wardrobe { Label = "Sunday best" } },
        }, c));

        Assert.Equal("A dock worker.", first.Character.Description);
        Assert.Equal("wiry", first.Character.Appearance.Build);
        Assert.Equal("dry", first.Character.Voice.Baseline);
        Assert.Equal(["work suit", "Sunday best"], first.Character.Wardrobes.Select(w => w.Label));

        var suit = first.Character.Wardrobes[0];
        var second = await _writer.Call((s, c) => s.UpdateCharacterSheet(new UpdateCharacterSheetRequest
        {
            CharacterId = mara.Id,
            Wardrobes = { new Wardrobe { Id = suit.Id, Label = "oily work suit", Description = "overalls" } },
        }, c));

        Assert.False(second.Character.HasDescription);
        Assert.Null(second.Character.Appearance);
        Assert.Equal([suit.Id], second.Character.Wardrobes.Select(w => w.Id));
        Assert.Equal("oily work suit", second.Character.Wardrobes[0].Label);
        Assert.Equal(second.Character, (await _writer.GetStoryAsync(story.Id)).Characters.Single());
    }

    [Fact]
    public async Task Location_variations_keep_their_ids_by_id_or_name_and_frames_there_go_back_to_the_establishing_shot_when_one_goes()
    {
        var story = await _writer.CreateStoryAsync();
        var wing = await _writer.CreateLocationAsync(story.Id, "Trauma wing");
        var first = await _writer.Call((s, c) => s.SaveLocationVariations(new SaveLocationVariationsRequest
        {
            LocationId = wing.Id,
            Variations =
            {
                new LocationVariation { Name = " Elevator end ", Description = "The lift doors under the exit sign." },
                new LocationVariation { Name = "Nurse station" },
            },
        }, c));
        Assert.Equal(["Elevator end", "Nurse station"], first.Location.Variations.Select(v => v.Name));
        var elevator = first.Location.Variations[0];
        var station = first.Location.Variations[1];

        var scene = await _writer.WriteFirstSceneAsync(story, Rows.Heading(wing.Id, "NIGHT"), Rows.Action("He waits."));
        var direction = new SceneDirection { SceneId = scene.Id };
        direction.Frames.Add(new Frame
        {
            FirstElementId = scene.Elements[0].Id, LastElementId = scene.Elements[1].Id, LocationVariationId = station.Id,
        });
        Assert.Equal(station.Id, (await _writer.SaveDirectionAsync(direction)).Frames[0].LocationVariationId);

        // Renamed by id, re-sent by name, the station left out.
        var second = await _writer.Call((s, c) => s.SaveLocationVariations(new SaveLocationVariationsRequest
        {
            LocationId = wing.Id,
            Variations =
            {
                new LocationVariation { Name = "Corridor mouth" },
                new LocationVariation { Id = elevator.Id, Name = "Lift end", Description = "The lift doors." },
            },
        }, c));

        Assert.Equal(["Corridor mouth", "Lift end"], second.Location.Variations.Select(v => v.Name));
        Assert.Equal(elevator.Id, second.Location.Variations[1].Id);
        Assert.Equal(second.Location, (await _writer.GetStoryAsync(story.Id)).Locations.Single());
        Assert.False((await _writer.GetDirectionAsync(scene.Id)).Frames[0].HasLocationVariationId);

        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.Call((s, c) => s.SaveLocationVariations(
            new SaveLocationVariationsRequest
            {
                LocationId = wing.Id,
                Variations = { new LocationVariation { Name = "Lift end" }, new LocationVariation { Name = "lift END" } },
            }, c)));
        Assert.Equal(StatusCode.AlreadyExists, error.StatusCode);
    }

    [Fact]
    public async Task Two_wardrobes_with_the_same_label_are_refused()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");

        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.Call((s, c) => s.UpdateCharacterSheet(
            new UpdateCharacterSheetRequest
            {
                CharacterId = mara.Id,
                Wardrobes = { new Wardrobe { Label = "Work suit" }, new Wardrobe { Label = "work suit" } },
            }, c)));

        Assert.Equal(StatusCode.AlreadyExists, error.StatusCode);
    }

    [Fact]
    public async Task Location_and_prop_sheets_round_trip_and_lighting_uses_the_vocabulary()
    {
        var story = await _writer.CreateStoryAsync();
        var docks = await _writer.CreateLocationAsync(story.Id, "Docks");
        var radio = await _writer.CreatePropAsync(story.Id, "Radio");

        var location = await _writer.Call((s, c) => s.UpdateLocationSheet(new UpdateLocationSheetRequest
        {
            LocationId = docks.Id,
            Description = "Cranes over black water.",
            Appearance = new LocationAppearance
            {
                Layout = "a long quay",
                KeyFeatures = "rusted cranes",
                Lighting = { new LocationLighting { TimeOfDay = "night", Description = "sodium lamps" } },
            },
            Acoustics = new Acoustics { Space = "open air", Ambience = "gulls, water" },
        }, c));
        var prop = await _writer.Call((s, c) => s.UpdatePropSheet(new UpdatePropSheetRequest { PropId = radio.Id, Description = "A dented field radio." }, c));

        Assert.Equal("NIGHT", location.Location.Appearance.Lighting[0].TimeOfDay);
        Assert.Equal("gulls, water", location.Location.Acoustics.Ambience);
        Assert.Equal("A dented field radio.", prop.Prop.Description);
        var saved = await _writer.GetStoryAsync(story.Id);
        Assert.Equal(location.Location, saved.Locations.Single());
        Assert.Equal(prop.Prop, saved.Props.Single());

        var error = await Assert.ThrowsAsync<RpcException>(() => _writer.Call((s, c) => s.UpdateLocationSheet(
            new UpdateLocationSheetRequest
            {
                LocationId = docks.Id,
                Appearance = new LocationAppearance { Lighting = { new LocationLighting { TimeOfDay = "TEATIME" } } },
            }, c)));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Fact]
    public async Task Delivery_tags_join_the_vocabulary_lowercased()
    {
        var story = await _writer.CreateStoryAsync();

        await _writer.Call((s, c) => s.SaveVocabulary(new SaveVocabularyRequest
        {
            StoryId = story.Id,
            Vocabulary = new StoryVocabulary { DeliveryTags = { " Wistful " } },
        }, c));

        Assert.Equal(["wistful"], (await _writer.GetStoryAsync(story.Id)).Vocabulary.DeliveryTags);
    }
}
