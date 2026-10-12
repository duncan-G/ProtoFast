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

        var prompt = WorldModelPrompt.Build(story, second, graph, facts);

        Assert.Contains($"<location id=\"{wing.Id}\" name=\"TRAUMA WING\"/>", prompt);
        Assert.Contains($"<character id=\"{bryan.Id}\" name=\"BRYAN\"/>", prompt);
        Assert.Contains("<prop name=\"cooler\"/>", prompt);
        Assert.Contains($"<part id=\"{start.Id}\" name=\"hallway: start\" position=\"0\"/>", prompt);
        Assert.Contains($"<object id=\"{gurney.Id}\" name=\"gurney\" kind=\"Prop\" aliases=\"the stretcher\" at=\"TRAUMA WING / hallway: start (part {start.Id})\"/>", prompt);
        Assert.Contains("<seen character=\"BRYAN\" at=\"PARKING LOT\"/>", prompt);
        Assert.Contains($"<beat id=\"{beat.Id}\" type=\"dialogue\" speaker=\"BRYAN\" parenthetical=\"shouting\">Where&#39;s the &lt;gurney&gt;?</beat>".Replace("&#39;", "'"), prompt);
        Assert.DoesNotContain("Bryan parks.", prompt);
    }

    private static ObservationRow Row(SceneInfo scene, int beat, ObservationEdge edge) =>
        new(Guid.NewGuid(), Guid.NewGuid(), scene.Id, new StoryTime(scene.Container, scene.Position, beat), edge,
            null, null, null, null, null, null, null, null, null, ObservationBasis.Stated, ObservationOrigin.Extracted);
}
