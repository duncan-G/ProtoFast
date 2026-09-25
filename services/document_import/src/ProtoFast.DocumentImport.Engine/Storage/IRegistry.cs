namespace ProtoFast.DocumentImport.Engine;

public interface IRegistry
{
    Task<Playbook>           ResolveAsync(PlaybookRef reference, CancellationToken ct);
    Task<ExecutorSpec>       ResolveAsync(ExecutorRef reference, CancellationToken ct);
    Task<WorkflowDefinition> ResolveAsync(WorkflowRef reference, CancellationToken ct);

    // Assigns the next version for the id, ignoring the one passed in.
    Task<PlaybookRef> PublishAsync(Playbook playbook, CancellationToken ct);
    Task<ExecutorRef> PublishAsync(ExecutorSpec spec, CancellationToken ct);
    Task<WorkflowRef> PublishAsync(WorkflowDefinition workflow, CancellationToken ct);

    Task PromoteAsync(ExecutorRef reference, CancellationToken ct);
    Task PromoteAsync(WorkflowRef reference, CancellationToken ct);
    Task<bool> IsPromotedAsync(WorkflowRef reference, CancellationToken ct);
}
