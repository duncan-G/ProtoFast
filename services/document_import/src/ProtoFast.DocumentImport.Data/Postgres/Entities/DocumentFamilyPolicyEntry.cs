using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class DocumentFamilyPolicyEntry
{
    public required string Family { get; set; }

    public RunMode Mode { get; set; }

    public string? WorkflowId { get; set; }

    public int? WorkflowVersion { get; set; }

    public double ConfidenceAlpha { get; set; }

    public double ConfidenceBeta { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
