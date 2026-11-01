using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.WorldModel;
using ProtoFast.DocumentImport.Worker.WorldModel;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.WorldModel;

public sealed class LocationDescriptionPromptTests
{
    [Fact]
    public void A_location_is_shown_as_the_story_first_finds_it_with_its_later_changes_apart()
    {
        var stop = new LocationInfo(Guid.NewGuid(), "TRUCK STOP", null, LocationSetting.Exterior);
        var diner = new LocationInfo(Guid.NewGuid(), "DINER", stop.Id, LocationSetting.Interior);
        var counter = new PartInfo(Guid.NewGuid(), diner.Id, null, "counter", null, null);
        var bar = new ObjectInfo(Guid.NewGuid(), "bar top", [], ObjectKind.Fixture, "chrome-edged");
        var sign = new ObjectInfo(Guid.NewGuid(), "neon sign", [], ObjectKind.Fixture, null);
        var glass = new ObjectInfo(Guid.NewGuid(), "broken glass", [], ObjectKind.Dressing, null);
        var pot = new ObjectInfo(Guid.NewGuid(), "coffee pot", [], ObjectKind.Prop, null);
        var crowd = new ObjectInfo(Guid.NewGuid(), "truckers", [], ObjectKind.Occupant, null);
        var arrival = Scene("Arrival", 0, stop.Id, "NIGHT", "A rig pulls in under the floodlights.");
        var closing = Scene("Closing", 1, diner.Id, "NIGHT", "Mae wipes down the bar.");
        var morning = Scene("Morning", 2, diner.Id, "DAY", "Daylight through the shattered window.");
        var leaving = Scene("Leaving", 3, stop.Id, "DAY", "The rig pulls out past the diner.");
        var story = new StorySnapshot(Guid.NewGuid(), "Night Shift", [stop, diner], [], [], [arrival, closing, morning, leaving]);
        var observations = new[]
        {
            Row(closing, ObservationEdge.At) with { ObjectId = bar.Id, PlaceLocationId = diner.Id, PlacePartId = counter.Id },
            Row(closing, ObservationEdge.At) with { ObjectId = sign.Id, PlaceLocationId = diner.Id },
            Row(closing, ObservationEdge.State) with { ObjectId = sign.Id, Property = "lit", Value = "on" },
            Row(closing, ObservationEdge.At) with { ObjectId = pot.Id, PlaceLocationId = diner.Id, PlacePartId = counter.Id },
            Row(closing, ObservationEdge.At) with { ObjectId = crowd.Id, PlaceLocationId = diner.Id },
            Row(morning, ObservationEdge.At) with { ObjectId = glass.Id, PlaceLocationId = diner.Id },
            Row(morning, ObservationEdge.State) with { ObjectId = sign.Id, Property = "lit", Value = "off" },
        };
        var graph = new GraphSnapshot([stop, diner], [counter], [bar, sign, glass, pot, crowd], observations);
        var kinds = graph.Objects.ToDictionary(o => o.Id, o => o.Kind);
        var facts = WorldModelFolder.Fold(observations, story.Scenes.Select(s => s.Slot).ToList(), kinds);

        var prompt = LocationDescriptionPrompt.Build(story, graph, facts, diner, ("TRUCK STOP", "A gravel lot off the interstate."));

        Assert.Contains("<story title=\"Night Shift\">", prompt);
        Assert.Contains("<scene n=\"4\" location=\"TRUCK STOP\" time=\"DAY\" title=\"Leaving\">The rig pulls out past the diner.</scene>", prompt);
        Assert.Contains("<location name=\"DINER\" setting=\"interior\" inside=\"TRUCK STOP\">", prompt);
        Assert.Contains("<outer name=\"TRUCK STOP\">A gravel lot off the interstate.</outer>", prompt);
        Assert.Contains("<part name=\"counter\"/>", prompt);
        Assert.Contains("<object name=\"bar top\" kind=\"Fixture\" at=\"DINER / counter\" description=\"chrome-edged\"/>", prompt);
        Assert.Contains("<object name=\"neon sign\" kind=\"Fixture\" at=\"DINER\" states=\"lit=on\"/>", prompt);
        Assert.Contains("<change scene=\"3\">broken glass (dressing) appears at DINER</change>", prompt);
        Assert.Contains("<change scene=\"3\">neon sign: lit becomes off</change>", prompt);
        Assert.DoesNotContain("coffee pot", prompt);
        Assert.DoesNotContain("truckers", prompt);
        Assert.Contains("<scene n=\"2\" title=\"Closing\" time=\"NIGHT\">", prompt);
        Assert.Contains("<beat>Daylight through the shattered window.</beat>", prompt);
        Assert.DoesNotContain("<beat>A rig pulls in under the floodlights.</beat>", prompt);
        Assert.Contains("<mentioned-elsewhere>\n  <beat scene=\"4\">The rig pulls out past the diner.</beat>", prompt.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_place_the_beats_go_to_under_another_heading_is_seen_there()
    {
        var hospital = new LocationInfo(Guid.NewGuid(), "HOSPITAL", null);
        var lobby = new LocationInfo(Guid.NewGuid(), "LOBBY", hospital.Id);
        var scene = Scene("Admissions", 0, hospital.Id, "DAY", "Nurses cross the lobby.");
        var story = new StorySnapshot(Guid.NewGuid(), "Ward", [hospital, lobby], [], [], [scene]);
        var graph = new GraphSnapshot([hospital, lobby], [], [], [Row(scene, ObservationEdge.BeatAt) with { PlaceLocationId = lobby.Id }]);

        Assert.Equal([scene.Id], LocationDescriptionPrompt.ScenesAt(story, graph, lobby.Id).Select(s => s.Id));
        var prompt = LocationDescriptionPrompt.Build(story, graph, WorldModelFolder.Fold(graph.Observations, [scene.Slot], new Dictionary<Guid, ObjectKind>()), lobby, null);
        Assert.Contains("<scene n=\"1\" title=\"Admissions\" heading=\"HOSPITAL\" time=\"DAY\">", prompt);
        Assert.DoesNotContain("<later-changes>", prompt);
    }

    [Fact]
    public void A_reply_is_read_past_its_fence()
    {
        var reading = LocationDescriber.Parse("""
            ```json
            {"description": "A narrow 1950s diner.", "contents": "Counter: a chrome-edged bar."}
            ```
            """);

        Assert.Equal(("A narrow 1950s diner.", "Counter: a chrome-edged bar."), (reading.Description, reading.Contents));
    }

    private static SceneInfo Scene(string title, int position, Guid locationId, string time, string text) =>
        new(Guid.NewGuid(), title, 0, position, locationId, time, [new BeatInfo(Guid.NewGuid(), SceneElementType.Action, text, null, null)]);

    private static ObservationRow Row(SceneInfo scene, ObservationEdge edge) =>
        new(Guid.NewGuid(), scene.Beats[0].Id, scene.Id, new StoryTime(scene.Container, scene.Position, 0), edge,
            null, null, null, null, null, null, null, null, null, ObservationBasis.Stated, ObservationOrigin.Extracted);
}
