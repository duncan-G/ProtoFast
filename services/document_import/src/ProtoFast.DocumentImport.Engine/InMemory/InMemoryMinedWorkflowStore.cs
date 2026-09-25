using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryMinedWorkflowStore : IMinedWorkflowStore
{
    private readonly ConcurrentDictionary<WorkflowRef, (string Family, MinedWorkflow Mined)> _drafts = new();

    public Task PutAsync(string family, MinedWorkflow mined, CancellationToken ct)
    {
        _drafts[mined.Workflow.Ref] = (family, mined);
        return Task.CompletedTask;
    }

    public Task<(string Family, MinedWorkflow Mined)?> GetAsync(WorkflowRef reference, CancellationToken ct) =>
        Task.FromResult<(string, MinedWorkflow)?>(_drafts.TryGetValue(reference, out var draft) ? draft : null);
}
