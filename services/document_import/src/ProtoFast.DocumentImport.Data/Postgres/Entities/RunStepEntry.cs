namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class RunStepEntry
{
    public required string RunId { get; set; }
    public int Sequence { get; set; }
    public required string Step { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
