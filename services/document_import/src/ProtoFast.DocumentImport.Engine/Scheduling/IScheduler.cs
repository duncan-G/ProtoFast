namespace ProtoFast.DocumentImport.Engine;

public interface IScheduler
{
    Task<RunSummary> RunAsync(WorkflowDefinition workflow, ArtifactRef input, CancellationToken ct);
}
