using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class RunEntry
{
    public required string RunId { get; set; }

    public required string Family { get; set; }

    public required string Facets { get; set; }

    public RunMode Mode { get; set; }

    public string? TraceId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
}
