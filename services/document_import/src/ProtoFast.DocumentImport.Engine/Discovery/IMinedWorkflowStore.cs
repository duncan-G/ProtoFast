namespace ProtoFast.DocumentImport.Engine;

public interface IMinedWorkflowStore
{
    Task PutAsync(string family, MinedWorkflow mined, CancellationToken ct);
    Task<(string Family, MinedWorkflow Mined)?> GetAsync(WorkflowRef reference, CancellationToken ct);
}
