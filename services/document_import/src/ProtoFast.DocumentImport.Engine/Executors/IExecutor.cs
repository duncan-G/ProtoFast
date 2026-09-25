namespace ProtoFast.DocumentImport.Engine;

public interface IExecutor
{
    Tier Tier { get; }
    Task<StageResult> ExecuteAsync(StageRequest request, CancellationToken ct);
}
