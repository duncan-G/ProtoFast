using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class RunProgressEntry
{
    public required string SourceId { get; set; }

    public RunPhase Phase { get; set; }

    public string? RunId { get; set; }

    public string? StageId { get; set; }

    public string? Message { get; set; }

    public string? ResultId { get; set; }

    public decimal Cost { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
