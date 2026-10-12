using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.WorldModel;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.WorldModel;

public sealed class CrossSceneConnectionsTests
{
    private static readonly Guid Lot = Guid.NewGuid();
    private static readonly Guid Wing = Guid.NewGuid();
    private static readonly Guid Gate = Guid.NewGuid();
    private static readonly Guid Bryan = Guid.NewGuid();
    private static readonly SceneSlot S1 = new(Guid.NewGuid(), Lot, 0, 0);
    private static readonly SceneSlot S2 = new(Guid.NewGuid(), Wing, 0, 1);

    [Fact]
    public void A_character_who_leaves_one_place_and_arrives_at_another_connects_them_through_the_way_out()
    {
        var arrival = Guid.NewGuid();
        List<ObservationRow> observations =
        [
            Row(S1, 0, ObservationEdge.CharacterAt) with { CharacterId = Bryan, PlaceLocationId = Lot },
            Row(S1, 1, ObservationEdge.Connects) with { PlaceLocationId = Lot, OtherLocationId = Guid.NewGuid(), ViaObjectId = Gate },
            Row(S2, 0, ObservationEdge.CharacterAt, arrival) with { CharacterId = Bryan, PlaceLocationId = Wing },
        ];
        var graph = WorldModelFolder.Fold(observations, [S1, S2], new Dictionary<Guid, ObjectKind>());

        var connection = Assert.Single(CrossSceneConnections.Find(observations, [S1, S2], graph));

        Assert.Equal((arrival, new PlaceRef(Lot, null), new PlaceRef(Wing, null), Gate), (connection.SceneElementId, connection.From, connection.To, connection.ViaObjectId));
    }

    [Fact]
    public void A_way_the_story_already_knows_is_not_added_again()
    {
        List<ObservationRow> observations =
        [
            Row(S1, 0, ObservationEdge.CharacterAt) with { CharacterId = Bryan, PlaceLocationId = Lot },
            Row(S1, 1, ObservationEdge.Connects) with { PlaceLocationId = Lot, OtherLocationId = Wing },
            Row(S2, 0, ObservationEdge.CharacterAt) with { CharacterId = Bryan, PlaceLocationId = Wing },
        ];
        var graph = WorldModelFolder.Fold(observations, [S1, S2], new Dictionary<Guid, ObjectKind>());

        Assert.Empty(CrossSceneConnections.Find(observations, [S1, S2], graph));
    }

    private static ObservationRow Row(SceneSlot scene, int beat, ObservationEdge edge, Guid? elementId = null) =>
        new(Guid.NewGuid(), elementId ?? Guid.NewGuid(), scene.SceneId, new StoryTime(scene.Container, scene.Position, beat), edge,
            null, null, null, null, null, null, null, null, null, ObservationBasis.Stated, ObservationOrigin.Extracted);
}
