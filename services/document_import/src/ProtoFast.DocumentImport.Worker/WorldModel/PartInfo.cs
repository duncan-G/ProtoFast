namespace ProtoFast.DocumentImport.Worker.WorldModel;

public sealed record PartInfo(Guid Id, Guid LocationId, Guid? ParentPartId, string? Name, string? Description, int? Position);
