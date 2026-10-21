namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>One edge a beat gives; which fields matter depends on <see cref="Edge"/>.</summary>
public sealed class ReadEdge
{
    public string? BeatId { get; set; }

    public string? Edge { get; set; }

    public string? ObjectId { get; set; }

    public string? CharacterId { get; set; }

    public string? LocationId { get; set; }

    public string? PartId { get; set; }

    public string? OtherLocationId { get; set; }

    public string? OtherPartId { get; set; }

    public string? ViaObjectId { get; set; }

    public string? Property { get; set; }

    public string? Value { get; set; }

    public string? Basis { get; set; }

    public string? NextPartId { get; set; }

    public string? InsideLocationId { get; set; }

    /// <summary>For a camera: the place it looks toward.</summary>
    public string? TowardLocationId { get; set; }

    public string? TowardPartId { get; set; }
}
