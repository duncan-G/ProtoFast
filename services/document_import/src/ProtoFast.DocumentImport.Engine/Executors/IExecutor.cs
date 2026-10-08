namespace ProtoFast.DocumentImport.Engine.Executors;

public interface IExecutor
{
    Tier Tier { get; }
    Task<StageResult> ExecuteAsync(StageRequest request, CancellationToken ct);
}
