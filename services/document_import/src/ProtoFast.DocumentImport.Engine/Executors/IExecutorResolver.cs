namespace ProtoFast.DocumentImport.Engine.Executors;

public interface IExecutorResolver
{
    Task<IExecutor> ResolveAsync(ExecutorRef reference, CancellationToken ct);
}
