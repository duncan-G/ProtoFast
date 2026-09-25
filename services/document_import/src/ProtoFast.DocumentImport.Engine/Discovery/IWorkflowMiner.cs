using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public interface IWorkflowMiner
{
    Task<MinedWorkflow?> MineAsync(string family, IReadOnlyList<RunSummary> runs, CancellationToken ct);
}
