namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class StageRecordEntry
{
    public long Sequence { get; set; }

    public required string RunId { get; set; }

    public required string StageId { get; set; }

    public required string Record { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}
