using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class StagePolicyEntry
{
    public required string Family { get; set; }

    public required string StageId { get; set; }

    public required string Ladder { get; set; }

    public Tier Primary { get; set; }

    public double ConfidenceAlpha { get; set; }

    public double ConfidenceBeta { get; set; }

    public Tier? Shadow { get; set; }

    public double ShadowAlpha { get; set; }

    public double ShadowBeta { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
