namespace ProtoFast.DocumentImport.Engine.Executors;

public interface IExecutorFactory
{
    bool CanBuild(ExecutorSpec spec);
    Task<IExecutor> BuildAsync(ExecutorSpec spec, CancellationToken ct);
}
