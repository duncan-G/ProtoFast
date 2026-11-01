using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

public sealed record ObjectInfo(Guid Id, string Name, IReadOnlyList<string> Aliases, ObjectKind Kind, string? Description, Guid? PropId = null);
