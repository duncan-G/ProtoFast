using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.DocumentImport.Worker.WorldModel;
using ProtoFast.Data.ThePlot.WorldModel;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.WorldModel;

public sealed class WorldModelPromptTests
{
    [Fact]
    public void The_scene_is_shown_with_what_its_location_holds_as_it_opens()
    {
        var wing = new LocationInfo(Guid.NewGuid(), "TRAUMA WING", null);
        var lot = new LocationInfo(Guid.NewGuid(), "PARKING LOT", null);
        var bryan = new CharacterInfo(Guid.NewGuid(), "BRYAN");
        var start = new PartInfo(Guid.NewGuid(), wing.Id, null, "hallway: start", null, 0);
        var gurney = new ObjectInfo(Guid.NewGuid(), "gurney", ["the stretcher"], ObjectKind.Prop, null);
        var first = new SceneInfo(Guid.NewGuid(), "Arrival", 0, 0, lot.Id, "NIGHT", [new BeatInfo(Guid.NewGuid(), SceneElementType.Action, "Bryan parks.", null, null)]);
        var beat = new BeatInfo(Guid.NewGuid(), SceneElementType.Dialogue, "Where's the <gurney>?", "BRYAN", "shouting");
        var second = new SceneInfo(Guid.NewGuid(), "The wing", 0, 1, wing.Id, "NIGHT", [beat]);
        var story = new StorySnapshot(Guid.NewGuid(), "Trauma", [lot, wing], [bryan], [new PropInfo(Guid.NewGuid(), "cooler")], [first, second]);
        var at = Row(first, 0, ObservationEdge.At) with { ObjectId = gurney.Id, PlaceLocationId = wing.Id, PlacePartId = start.Id };
        var seen = Row(first, 0, ObservationEdge.CharacterAt) with { CharacterId = bryan.Id, PlaceLocationId = lot.Id };
        var graph = new GraphSnapshot([lot, wing], [start], [gurney], [at, seen]);
        var facts = WorldModelFolder.Fold(graph.Observations, [first.Slot, second.Slot], new Dictionary<Guid, ObjectKind> { [gurney.Id] = ObjectKind.Prop });

        var prompt = WorldModelPrompt.Build(story, second, graph, facts, new HashSet<Guid>());

        Assert.Contains($"<location id=\"{wing.Id}\" name=\"TRAUMA WING\"/>", prompt);
        Assert.Contains($"<character id=\"{bryan.Id}\" name=\"BRYAN\"/>", prompt);
        Assert.Contains("<prop name=\"cooler\"/>", prompt);
        Assert.Contains($"<part id=\"{start.Id}\" name=\"hallway: start\" position=\"0\"/>", prompt);
        Assert.Contains($"<object id=\"{gurney.Id}\" name=\"gurney\" kind=\"Prop\" aliases=\"the stretcher\" at=\"TRAUMA WING / hallway: start (part {start.Id})\"/>", prompt);
        Assert.Contains("<seen character=\"BRYAN\" at=\"PARKING LOT\"/>", prompt);
        Assert.Contains($"<beat id=\"{beat.Id}\" type=\"dialogue\" speaker=\"BRYAN\" parenthetical=\"shouting\">Where&#39;s the &lt;gurney&gt;?</beat>".Replace("&#39;", "'"), prompt);
        Assert.DoesNotContain($"<beat id=\"{first.Beats[0].Id}\"", prompt);
        Assert.Contains("<previous-scene title=\"Arrival\" location=\"PARKING LOT\" time=\"NIGHT\">", prompt);
        Assert.Contains("<text>Bryan parks.</text>", prompt);
    }

    [Fact]
    public void The_scene_is_shown_with_the_places_around_it_their_fixtures_and_props_last_seen_elsewhere()
    {
        var hospital = new LocationInfo(Guid.NewGuid(), "HOSPITAL", null);
        var lobby = new LocationInfo(Guid.NewGuid(), "LOBBY", hospital.Id);
        var wing = new LocationInfo(Guid.NewGuid(), "TRAUMA WING", hospital.Id);
        var cooler = new ObjectInfo(Guid.NewGuid(), "refrigerated cooler", ["the cooler"], ObjectKind.Prop, null);
        var counter = new ObjectInfo(Guid.NewGuid(), "counter", [], ObjectKind.Fixture, null);
        var first = new SceneInfo(Guid.NewGuid(), "Delivery", 0, 0, lobby.Id, "DUSK", [new BeatInfo(Guid.NewGuid(), SceneElementType.Action, "In strides Bryan.", null, null)]);
        var second = new SceneInfo(Guid.NewGuid(), "The wing", 0, 1, wing.Id, "CONTINUOUS", [new BeatInfo(Guid.NewGuid(), SceneElementType.Action, "Bryan walks.", null, null)]);
        var story = new StorySnapshot(Guid.NewGuid(), "Trauma", [hospital, lobby, wing], [], [], [first, second]);
        var observations = new[]
        {
            Row(first, 0, ObservationEdge.At) with { ObjectId = cooler.Id, PlaceLocationId = lobby.Id },
            Row(first, 0, ObservationEdge.At) with { ObjectId = counter.Id, PlaceLocationId = lobby.Id },
        };
        var graph = new GraphSnapshot([hospital, lobby, wing], [], [cooler, counter], observations);
        var facts = WorldModelFolder.Fold(observations, [first.Slot, second.Slot], new Dictionary<Guid, ObjectKind> { [cooler.Id] = ObjectKind.Prop, [counter.Id] = ObjectKind.Fixture });

        var prompt = WorldModelPrompt.Build(story, second, graph, facts, new HashSet<Guid>());

        Assert.Contains($"<location id=\"{hospital.Id}\" name=\"HOSPITAL\" relation=\"contains this place\"/>", prompt);
        Assert.Contains($"<location id=\"{lobby.Id}\" name=\"LOBBY\" relation=\"inside the same place\">", prompt);
        Assert.Contains($"<object id=\"{counter.Id}\" name=\"counter\" at=\"LOBBY\"/>", prompt);
        Assert.Contains($"<object id=\"{cooler.Id}\" name=\"refrigerated cooler\" aliases=\"the cooler\" at=\"LOBBY\"/>", prompt);
        Assert.DoesNotContain($"name=\"counter\" kind=", prompt);
    }

    [Fact]
    public void Voices_cuts_and_what_a_character_carried_are_shown()
    {
        var hallway = new LocationInfo(Guid.NewGuid(), "HALLWAY", null);
        var apartment = new LocationInfo(Guid.NewGuid(), "APARTMENT", null);
        var happy = new CharacterInfo(Guid.NewGuid(), "HAPPY");
        var bag = new ObjectInfo(Guid.NewGuid(), "gym bag", [], ObjectKind.Prop, null);
        var first = new SceneInfo(Guid.NewGuid(), "Hallway", 0, 0, hallway.Id, "NIGHT", [new BeatInfo(Guid.NewGuid(), SceneElementType.Action, "Happy walks out.", null, null)]);
        var narration = new BeatInfo(Guid.NewGuid(), SceneElementType.Dialogue, "I loved hockey.", "HAPPY", null, happy.Id, "V.O.");
        var photo = new BeatInfo(Guid.NewGuid(), SceneElementType.Action, "A photo of a hockey team.", null, null, Cut: "CUT TO");
        var second = new SceneInfo(Guid.NewGuid(), "Apartment", 0, 1, apartment.Id, "NIGHT", [narration, photo]);
        var story = new StorySnapshot(Guid.NewGuid(), "Happy", [hallway, apartment], [happy], [], [first, second]);
        var observations = new[]
        {
            Row(first, 0, ObservationEdge.CharacterAt) with { CharacterId = happy.Id, PlaceLocationId = hallway.Id },
            Row(first, 0, ObservationEdge.At) with { ObjectId = bag.Id, CharacterId = happy.Id, PlaceLocationId = hallway.Id },
        };
        var graph = new GraphSnapshot([hallway, apartment], [], [bag], observations);
        var facts = WorldModelFolder.Fold(observations, [first.Slot, second.Slot], new Dictionary<Guid, ObjectKind> { [bag.Id] = ObjectKind.Prop });

        var prompt = WorldModelPrompt.Build(story, second, graph, facts, new HashSet<Guid>());

        Assert.Contains($"<seen character=\"HAPPY\" at=\"HALLWAY\" carrying=\"gym bag ({bag.Id})\"/>", prompt);
        Assert.Contains($"<beat id=\"{narration.Id}\" type=\"dialogue\" speaker=\"HAPPY\" extension=\"V.O.\">I loved hockey.</beat>", prompt);
        Assert.Contains($"<cut>CUT TO</cut>\n  <beat id=\"{photo.Id}\"", prompt.ReplaceLineEndings("\n"));
        Assert.True(narration.IsVoiceOver);
    }

    [Fact]
    public void A_flashback_is_not_where_anyone_was_last_seen_and_the_scenes_around_are_shown()
    {
        var ship = new LocationInfo(Guid.NewGuid(), "SHIP", null);
        var corridor = new LocationInfo(Guid.NewGuid(), "CORRIDOR", ship.Id);
        var bar = new LocationInfo(Guid.NewGuid(), "BAR", null);
        var grace = new CharacterInfo(Guid.NewGuid(), "GRACE");
        var hatch = new ObjectInfo(Guid.NewGuid(), "hatch", [], ObjectKind.Fixture, null, Guid.NewGuid());
        var awake = new SceneInfo(Guid.NewGuid(), "Awake", 0, 0, corridor.Id, "DAY", [new BeatInfo(Guid.NewGuid(), SceneElementType.Action, "Grace finds a hatch.", null, null)]);
        var memory = new SceneInfo(Guid.NewGuid(), "Bar", 0, 1, bar.Id, "DAY", [new BeatInfo(Guid.NewGuid(), SceneElementType.Action, "Grace watches the news.", null, null)]);
        var back = new SceneInfo(Guid.NewGuid(), "Ship", 0, 2, ship.Id, "DAY", [new BeatInfo(Guid.NewGuid(), SceneElementType.Action, "He crosses to the hatch.", null, null)]);
        var story = new StorySnapshot(Guid.NewGuid(), "Hail", [ship, corridor, bar], [grace], [], [awake, memory, back]);
        var observations = new[]
        {
            Row(awake, 0, ObservationEdge.CharacterAt) with { CharacterId = grace.Id, PlaceLocationId = corridor.Id },
            Row(awake, 0, ObservationEdge.At) with { ObjectId = hatch.Id, PlaceLocationId = corridor.Id },
            Row(memory, 0, ObservationEdge.CharacterAt) with { CharacterId = grace.Id, PlaceLocationId = bar.Id },
        };
        var graph = new GraphSnapshot([ship, corridor, bar], [], [hatch], observations);
        var facts = WorldModelFolder.Fold(observations, [awake.Slot, memory.Slot, back.Slot], new Dictionary<Guid, ObjectKind> { [hatch.Id] = ObjectKind.Fixture });

        var prompt = WorldModelPrompt.Build(story, back, graph, facts, new HashSet<Guid> { memory.Id });

        Assert.Contains("<seen character=\"GRACE\" at=\"CORRIDOR\"/>", prompt);
        Assert.Contains($"<location id=\"{corridor.Id}\" name=\"CORRIDOR\" relation=\"inside this place\">", prompt);
        Assert.Contains($"<object id=\"{hatch.Id}\" name=\"hatch\" at=\"CORRIDOR\"/>", prompt);
        Assert.Contains("<previous-scene title=\"Bar\" location=\"BAR\" time=\"DAY\" flashback=\"true\">", prompt);
        Assert.Contains("<text>Grace watches the news.</text>", prompt);
        Assert.DoesNotContain("<next-scene", prompt);
    }

    private static ObservationRow Row(SceneInfo scene, int beat, ObservationEdge edge) =>
        new(Guid.NewGuid(), Guid.NewGuid(), scene.Id, new StoryTime(scene.Container, scene.Position, beat), edge,
            null, null, null, null, null, null, null, null, null, ObservationBasis.Stated, ObservationOrigin.Extracted);
}
