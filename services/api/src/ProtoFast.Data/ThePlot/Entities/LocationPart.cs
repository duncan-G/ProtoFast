namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A part of a location the beats name ("hallway: start", "trauma bay"). Owned by its
/// <see cref="Location"/>: it has no <c>UserId</c> and goes when the location does.
/// </summary>
public sealed class LocationPart
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public Location Location { get; set; } = null!;

    /// <summary>Null when the part is directly under the location.</summary>
    public Guid? ParentPartId { get; set; }

    public LocationPart? ParentPart { get; set; }

    /// <summary>Null for an unnamed part ("a room off the hallway").</summary>
    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Order among sibling parts of a linear space; null when the parts have no order.</summary>
    public int? Position { get; set; }

    public NodeOrigin Origin { get; set; }
}
