namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class RunMessageEntry
{
    public required string RunId { get; set; }

    public int Sequence { get; set; }

    public required string Message { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}
