namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One claim a beat makes about the world: an object is somewhere, has a state, two places
/// connect, a character or the beat itself is at a place. Owned by its beat: it has no
/// <c>UserId</c>, and re-reading or deleting the beat replaces its rows. <see cref="Edge"/>
/// decides which columns are set; an <see cref="ObservationEdge.At"/> with no place means the
/// object left.
/// </summary>
public sealed class LocationObservation
{
    public Guid Id { get; set; }

    public Guid SceneElementId { get; set; }

    public SceneElement SceneElement { get; set; } = null!;

    public ObservationEdge Edge { get; set; }

    public Guid? ObjectId { get; set; }

    public LocationObject? Object { get; set; }

    public Guid? CharacterId { get; set; }

    public Character? Character { get; set; }

    public Guid? PlaceLocationId { get; set; }

    public Location? PlaceLocation { get; set; }

    /// <summary>Null for the whole location.</summary>
    public Guid? PlacePartId { get; set; }

    public LocationPart? PlacePart { get; set; }

    /// <summary>The other end of a <see cref="ObservationEdge.Connects"/>.</summary>
    public Guid? OtherLocationId { get; set; }

    public Location? OtherLocation { get; set; }

    public Guid? OtherPartId { get; set; }

    public LocationPart? OtherPart { get; set; }

    /// <summary>What a <see cref="ObservationEdge.Connects"/> goes through: a door, a gate.</summary>
    public Guid? ViaObjectId { get; set; }

    public LocationObject? ViaObject { get; set; }

    public string? Property { get; set; }

    /// <summary>A state's value; "closed" on a <see cref="ObservationEdge.Connects"/> ends the connection.</summary>
    public string? Value { get; set; }

    public ObservationBasis Basis { get; set; }

    public ObservationOrigin Origin { get; set; }
}
