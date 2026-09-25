using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Engine.Workflows;

public readonly record struct WorkflowRef(string Id, int Version)
{
    // Outcomes are attributed to an executor, so a family-level outcome names its workflow as one.
    public ExecutorRef AsExecutor() => new($"workflow:{Id}", Version);
}
