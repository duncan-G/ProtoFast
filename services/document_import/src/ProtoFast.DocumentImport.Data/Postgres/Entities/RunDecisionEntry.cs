namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class RunDecisionEntry
{
    public long Sequence { get; set; }

    public required string RunId { get; set; }

    public required string Key { get; set; }

    public required string Choice { get; set; }

    public required string Rationale { get; set; }

    public double Confidence { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}
