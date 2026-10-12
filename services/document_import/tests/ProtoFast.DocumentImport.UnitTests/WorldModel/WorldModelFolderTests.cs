using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Data.ThePlot.WorldModel;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.WorldModel;

/// <summary>The walk of design doc §5 and the time rules of §4, on the doc's own example.</summary>
public sealed class WorldModelFolderTests
{
    private static readonly Guid Wing = Guid.NewGuid();
    private static readonly Guid Lot = Guid.NewGuid();
    private static readonly Guid Start = Guid.NewGuid();
    private static readonly Guid Middle = Guid.NewGuid();
    private static readonly Guid End = Guid.NewGuid();
    private static readonly Guid Bay = Guid.NewGuid();
    private static readonly Guid Doors = Guid.NewGuid();
    private static readonly Guid Gurney = Guid.NewGuid();
    private static readonly Guid Light = Guid.NewGuid();
    private static readonly Guid Nurses = Guid.NewGuid();
    private static readonly Guid Bryan = Guid.NewGuid();

    private static readonly SceneSlot S1 = new(Guid.NewGuid(), Lot, 0, 0);
    private static readonly SceneSlot S2 = new(Guid.NewGuid(), Wing, 0, 1);
    private static readonly SceneSlot S5 = new(Guid.NewGuid(), Wing, 0, 4);

    private static readonly Dictionary<Guid, ObjectKind> Kinds = new()
    {
        [Doors] = ObjectKind.Fixture,
        [Gurney] = ObjectKind.Prop,
        [Light] = ObjectKind.Fixture,
        [Nurses] = ObjectKind.Occupant,
    };

    [Fact]
    public void Facts_carry_forward_until_a_later_beat_changes_them()
    {
        var graph = WorldModelFolder.Fold(
        [
            At(S2, 1, Gurney, Wing, Middle),
            State(S2, 6, Light, "lit", "flickering"),
            At(S2, 6, Light, Wing, End),
            At(S5, 2, Gurney, null, null),
            State(S5, 4, Light, "lit", "steady"),
        ], [S1, S2, S5], Kinds);

        var openingOfS5 = S5.Start;
        Assert.Contains(graph.ObjectsIn(Wing, openingOfS5), f => f.ObjectId == Gurney && f.Place == new PlaceRef(Wing, Middle));
        Assert.Equal("flickering", Assert.Single(graph.StatesOf(Light, openingOfS5)).Value);

        var afterS5 = S5.End;
        Assert.DoesNotContain(graph.ObjectsIn(Wing, afterS5), f => f.ObjectId == Gurney);
        Assert.Equal("steady", Assert.Single(graph.StatesOf(Light, afterS5)).Value);
        Assert.Empty(graph.Conflicts);
    }

    [Fact]
    public void A_stated_observation_beats_an_implied_one_and_an_authors_beats_both()
    {
        var graph = WorldModelFolder.Fold(
        [
            At(S2, 1, Gurney, Wing, Middle, ObservationBasis.Stated),
            At(S2, 3, Gurney, Wing, End, ObservationBasis.Implied),
            At(S2, 5, Gurney, Wing, Start, ObservationBasis.Implied, ObservationOrigin.Author),
            At(S2, 7, Gurney, Wing, Bay, ObservationBasis.Stated),
        ], [S1, S2, S5], Kinds);

        var place = Assert.Single(graph.ObjectsIn(Wing, S5.Start), f => f.ObjectId == Gurney).Place;
        Assert.Equal(new PlaceRef(Wing, Start), place);
        Assert.Equal(2, graph.Conflicts.Count);
    }

    [Fact]
    public void An_occupant_leaves_with_its_scene_and_a_fixture_was_always_there()
    {
        var graph = WorldModelFolder.Fold(
        [
            At(S2, 2, Nurses, Wing, Start),
            At(S5, 3, Doors, Wing, Start),
        ], [S1, S2, S5], Kinds);

        Assert.Contains(graph.ObjectsIn(Wing, new StoryTime(0, 1, 9)), f => f.ObjectId == Nurses);
        Assert.DoesNotContain(graph.ObjectsIn(Wing, S5.Start), f => f.ObjectId == Nurses);
        Assert.Contains(graph.ObjectsIn(Wing, S2.Start), f => f.ObjectId == Doors);
        Assert.DoesNotContain(graph.ObjectsIn(Lot, S1.Start), f => f.ObjectId == Doors);
    }

    [Fact]
    public void A_connection_holds_either_way_round_until_it_closes()
    {
        var graph = WorldModelFolder.Fold(
        [
            Connects(S2, 6, Wing, End, Wing, Bay, Doors),
            Connects(S5, 1, Wing, Bay, Wing, End, null, "closed"),
        ], [S1, S2, S5], Kinds);

        Assert.True(graph.Connected(new PlaceRef(Wing, Bay), new PlaceRef(Wing, End), S5.Start));
        Assert.Equal(Doors, Assert.Single(graph.ConnectionsOf(Wing, S5.Start)).ViaObjectId);
        Assert.False(graph.Connected(new PlaceRef(Wing, End), new PlaceRef(Wing, Bay), S5.End));
    }

    [Fact]
    public void Beats_and_characters_keep_their_last_place()
    {
        var graph = WorldModelFolder.Fold(
        [
            CharacterAt(S1, 0, Bryan, Lot, null),
            BeatAt(S2, 0, Wing, Start),
            CharacterAt(S2, 0, Bryan, Wing, Start),
            BeatAt(S2, 3, Wing, Middle),
        ], [S1, S2, S5], Kinds);

        Assert.Equal(new PlaceRef(Wing, Start), graph.BeatPlace(Wing, new StoryTime(0, 1, 2)));
        Assert.Equal(new PlaceRef(Wing, Middle), graph.BeatPlace(Wing, S5.Start));
        Assert.Equal(new PlaceRef(Lot, null), graph.CharactersAt(S2.Start)[Bryan].Place);
        Assert.Equal(new PlaceRef(Wing, Start), graph.CharactersAt(S5.Start)[Bryan].Place);
    }

    private static ObservationRow At(
        SceneSlot scene, int beat, Guid objectId, Guid? location, Guid? part,
        ObservationBasis basis = ObservationBasis.Stated, ObservationOrigin origin = ObservationOrigin.Extracted) =>
        Row(scene, beat, ObservationEdge.At, basis, origin) with { ObjectId = objectId, PlaceLocationId = location, PlacePartId = part };

    private static ObservationRow State(SceneSlot scene, int beat, Guid objectId, string property, string value) =>
        Row(scene, beat, ObservationEdge.State, ObservationBasis.Stated, ObservationOrigin.Extracted) with
        {
            ObjectId = objectId, Property = property, Value = value,
        };

    private static ObservationRow Connects(SceneSlot scene, int beat, Guid location, Guid? part, Guid other, Guid? otherPart, Guid? via, string? value = null) =>
        Row(scene, beat, ObservationEdge.Connects, ObservationBasis.Stated, ObservationOrigin.Extracted) with
        {
            PlaceLocationId = location, PlacePartId = part, OtherLocationId = other, OtherPartId = otherPart, ViaObjectId = via, Value = value,
        };

    private static ObservationRow CharacterAt(SceneSlot scene, int beat, Guid character, Guid location, Guid? part) =>
        Row(scene, beat, ObservationEdge.CharacterAt, ObservationBasis.Implied, ObservationOrigin.Extracted) with
        {
            CharacterId = character, PlaceLocationId = location, PlacePartId = part,
        };

    private static ObservationRow BeatAt(SceneSlot scene, int beat, Guid location, Guid? part) =>
        Row(scene, beat, ObservationEdge.BeatAt, ObservationBasis.Implied, ObservationOrigin.Extracted) with
        {
            PlaceLocationId = location, PlacePartId = part,
        };

    private static ObservationRow Row(SceneSlot scene, int beat, ObservationEdge edge, ObservationBasis basis, ObservationOrigin origin) =>
        new(Guid.NewGuid(), Guid.NewGuid(), scene.SceneId, new StoryTime(scene.Container, scene.Position, beat), edge,
            null, null, null, null, null, null, null, null, null, basis, origin);
}
