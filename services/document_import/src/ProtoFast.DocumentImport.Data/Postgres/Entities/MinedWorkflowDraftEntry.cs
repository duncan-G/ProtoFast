namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class MinedWorkflowDraftEntry
{
    public required string WorkflowId { get; set; }

    public int WorkflowVersion { get; set; }

    public required string Family { get; set; }

    public required string Mined { get; set; }

    public DateTimeOffset MinedAt { get; set; }
}
