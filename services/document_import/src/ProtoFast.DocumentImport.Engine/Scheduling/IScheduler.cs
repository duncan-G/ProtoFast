using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Scheduling;

public interface IScheduler
{
    Task<RunSummary> RunAsync(WorkflowDefinition workflow, ArtifactRef input, CancellationToken ct);
}
