namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One part of a location and the view from there: "Elevator end, looking at the lift doors". Frames
/// that happen there take their background from its picture instead of the location's establishing
/// shot. Owned by its <see cref="Location"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class LocationVariation
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public Location Location { get; set; } = null!;

    public int Position { get; set; }

    /// <summary>Unique per location; a frame's directing names it.</summary>
    public required string Name { get; set; }

    /// <summary>Where in the place the camera stands and what it sees from there.</summary>
    public string Description { get; set; } = "";

    /// <summary>The part pictured empty, if generated; drawn from the location's own picture.</summary>
    public Guid? ImageAssetId { get; set; }

    public MediaAsset? Image { get; set; }
}
