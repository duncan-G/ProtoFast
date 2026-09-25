namespace ProtoFast.DocumentImport.Data.Entities;

public sealed class DocumentFamilyVerifier
{
    public required string Family { get; set; }

    public required string VerifierId { get; set; }

    public required string StageId { get; set; }

    public required string Rubric { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
