using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.DocumentImport.Worker.WorldModel;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.WorldModel;

/// <summary>A held object goes where its holder goes, beat by beat, until it is put down or handed over.</summary>
public sealed class HeldObjectsTests
{
    private static readonly Guid Hallway = Guid.NewGuid();
    private static readonly Guid Floor = Guid.NewGuid();
    private static readonly Guid Elevator = Guid.NewGuid();
    private static readonly Guid Terry = Guid.NewGuid();
    private static readonly Guid Happy = Guid.NewGuid();
    private static readonly Guid Duffel = Guid.NewGuid();

    private static readonly BeatInfo[] Beats =
    [
        new(Guid.NewGuid(), SceneElementType.Action, "Terry waits with a duffel bag.", null, null),
        new(Guid.NewGuid(), SceneElementType.Action, "She steps into the elevator.", null, null),
        new(Guid.NewGuid(), SceneElementType.Action, "She hands Happy the bag.", null, null),
        new(Guid.NewGuid(), SceneElementType.Action, "Terry steps out. Happy follows.", null, null),
    ];

    private static readonly SceneInfo Scene = new(Guid.NewGuid(), "Hallway", 0, 0, Hallway, "NIGHT", Beats);

    [Fact]
    public void A_held_object_follows_its_holder_until_handed_over()
    {
        var taken = At(0, Duffel, holder: Terry, part: null);
        var rows = new List<LocationObservation>
        {
            CharacterAt(0, Terry, Floor),
            taken,
            CharacterAt(1, Terry, Elevator),
            At(2, Duffel, holder: Happy, part: Elevator),
            CharacterAt(3, Terry, Floor),
            CharacterAt(3, Happy, Floor),
        };

        var carried = ObservationApplier.Carry(Scene, rows, Hallway).ToList();

        Assert.Equal(Floor, taken.PlacePartId);
        Assert.Collection(
            carried,
            c => Assert.Equal((Beats[1].Id, Terry, Elevator), (c.SceneElementId, c.CharacterId!.Value, c.PlacePartId!.Value)),
            c => Assert.Equal((Beats[3].Id, Happy, Floor), (c.SceneElementId, c.CharacterId!.Value, c.PlacePartId!.Value)));
        Assert.All(carried, c => Assert.Equal(ObservationBasis.Implied, c.Basis));
    }

    [Fact]
    public void An_object_put_down_stays_when_its_holder_moves()
    {
        var rows = new List<LocationObservation>
        {
            CharacterAt(0, Terry, Floor),
            At(0, Duffel, holder: Terry, part: Floor),
            At(1, Duffel, holder: null, part: Floor),
            CharacterAt(2, Terry, Elevator),
        };

        Assert.Empty(ObservationApplier.Carry(Scene, rows, Hallway));
    }

    private static LocationObservation CharacterAt(int beat, Guid character, Guid part) => new()
    {
        Id = Guid.NewGuid(), SceneElementId = Beats[beat].Id, Edge = ObservationEdge.CharacterAt, CharacterId = character,
        PlaceLocationId = Hallway, PlacePartId = part, Basis = ObservationBasis.Stated, Origin = ObservationOrigin.Extracted,
    };

    private static LocationObservation At(int beat, Guid objectId, Guid? holder, Guid? part) => new()
    {
        Id = Guid.NewGuid(), SceneElementId = Beats[beat].Id, Edge = ObservationEdge.At, ObjectId = objectId, CharacterId = holder,
        PlaceLocationId = part is null ? null : Hallway, PlacePartId = part, Basis = ObservationBasis.Stated, Origin = ObservationOrigin.Extracted,
    };
}
