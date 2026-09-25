namespace ProtoFast.DocumentImport.Engine;

public interface IExecutorResolver
{
    Task<IExecutor> ResolveAsync(ExecutorRef reference, CancellationToken ct);
}
