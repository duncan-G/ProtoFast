namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class DocumentFamilyEntry
{
    public required string Family { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
