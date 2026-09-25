using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public interface IMinedWorkflowStore
{
    Task PutAsync(string family, MinedWorkflow mined, CancellationToken ct);
    Task<(string Family, MinedWorkflow Mined)?> GetAsync(WorkflowRef reference, CancellationToken ct);
}
