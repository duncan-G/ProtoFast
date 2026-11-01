namespace ProtoFast.Data.ThePlot.WorldModel;

/// <summary>A location, or a part of one when <see cref="PartId"/> is set.</summary>
public readonly record struct PlaceRef(Guid LocationId, Guid? PartId)
{
    public override string ToString() => PartId is { } part ? $"{LocationId}/{part}" : $"{LocationId}/-";
}
