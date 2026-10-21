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
    public void A_character_who_leaves_one_place_and_turns_up_at_another_in_the_next_scene_connects_them()
    {
        var arrival = Guid.NewGuid();
        List<ObservationRow> observations =
        [
            Row(S1, 0, ObservationEdge.CharacterAt) with { CharacterId = Bryan, PlaceLocationId = Lot },
            Row(S1, 1, ObservationEdge.Connects) with { PlaceLocationId = Lot, OtherLocationId = Guid.NewGuid(), ViaObjectId = Gate },
            Row(S2, 0, ObservationEdge.CharacterAt, arrival) with { CharacterId = Bryan, PlaceLocationId = Wing },
        ];
        var graph = WorldModelFolder.Fold(observations, [S1, S2], new Dictionary<Guid, ObjectKind>());

        var connection = Assert.Single(CrossSceneConnections.Find(observations, [S1, S2], graph, new HashSet<Guid> { S2.SceneId }));

        Assert.Equal((arrival, new PlaceRef(Lot, null), new PlaceRef(Wing, null)), (connection.SceneElementId, connection.From, connection.To));
    }

    [Fact]
    public void A_scene_that_does_not_follow_on_gives_no_way_through()
    {
        List<ObservationRow> observations =
        [
            Row(S1, 0, ObservationEdge.CharacterAt) with { CharacterId = Bryan, PlaceLocationId = Lot },
            Row(S2, 0, ObservationEdge.CharacterAt) with { CharacterId = Bryan, PlaceLocationId = Wing },
        ];
        var graph = WorldModelFolder.Fold(observations, [S1, S2], new Dictionary<Guid, ObjectKind>());

        Assert.Empty(CrossSceneConnections.Find(observations, [S1, S2], graph, new HashSet<Guid>()));
    }

    [Theory]
    [InlineData("CONTINUOUS", true)]
    [InlineData("moments later", true)]
    [InlineData("NIGHT", false)]
    [InlineData("LATER", false)]
    [InlineData(null, false)]
    public void A_heading_time_says_whether_a_scene_follows_on(string? time, bool followsOn) =>
        Assert.Equal(followsOn, SceneContinuity.FollowsOn(time));

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

        Assert.Empty(CrossSceneConnections.Find(observations, [S1, S2], graph, new HashSet<Guid> { S2.SceneId }));
    }

    [Fact]
    public void A_way_between_other_parts_of_the_two_locations_is_enough()
    {
        List<ObservationRow> observations =
        [
            Row(S1, 0, ObservationEdge.CharacterAt) with { CharacterId = Bryan, PlaceLocationId = Lot, PlacePartId = Guid.NewGuid() },
            Row(S1, 1, ObservationEdge.Connects) with { PlaceLocationId = Lot, PlacePartId = Guid.NewGuid(), OtherLocationId = Wing },
            Row(S2, 0, ObservationEdge.CharacterAt) with { CharacterId = Bryan, PlaceLocationId = Wing, PlacePartId = Guid.NewGuid() },
        ];
        var graph = WorldModelFolder.Fold(observations, [S1, S2], new Dictionary<Guid, ObjectKind>());

        Assert.Empty(CrossSceneConnections.Find(observations, [S1, S2], graph, new HashSet<Guid> { S2.SceneId }));
    }

    private static ObservationRow Row(SceneSlot scene, int beat, ObservationEdge edge, Guid? elementId = null) =>
        new(Guid.NewGuid(), elementId ?? Guid.NewGuid(), scene.SceneId, new StoryTime(scene.Container, scene.Position, beat), edge,
            null, null, null, null, null, null, null, null, null, ObservationBasis.Stated, ObservationOrigin.Extracted);
}
