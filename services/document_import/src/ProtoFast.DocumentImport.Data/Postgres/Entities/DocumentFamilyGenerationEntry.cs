namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class DocumentFamilyGenerationEntry
{
    public required string Family { get; set; }

    public int Generation { get; set; }

    public DateTimeOffset ResetAt { get; set; }
}
