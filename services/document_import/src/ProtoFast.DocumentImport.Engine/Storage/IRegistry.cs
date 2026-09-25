using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Storage;

public interface IRegistry
{
    Task<Playbook>           ResolveAsync(PlaybookRef reference, CancellationToken ct);
    Task<ExecutorSpec>       ResolveAsync(ExecutorRef reference, CancellationToken ct);
    Task<WorkflowDefinition> ResolveAsync(WorkflowRef reference, CancellationToken ct);

    // Assigns the next version for the id, ignoring the one passed in.
    Task<PlaybookRef> PublishAsync(Playbook playbook, CancellationToken ct);
    Task<ExecutorRef> PublishAsync(ExecutorSpec spec, CancellationToken ct);
    Task<WorkflowRef> PublishAsync(WorkflowDefinition workflow, CancellationToken ct);

    // Returns the code's SHA-256, which ExecutorSpec.CodeAssembly names.
    Task<string> PublishCodeAsync(Stream code, CancellationToken ct);
    Task<Stream> OpenCodeAsync(string hash, CancellationToken ct);
    Task<bool> CodeExistsAsync(string hash, CancellationToken ct);

    Task PromoteAsync(ExecutorRef reference, CancellationToken ct);
    Task PromoteAsync(WorkflowRef reference, CancellationToken ct);
    Task<bool> IsPromotedAsync(WorkflowRef reference, CancellationToken ct);
}
