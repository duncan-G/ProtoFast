namespace ProtoFast.DocumentImport.Engine;

public interface IWorkflowMiner
{
    Task<MinedWorkflow?> MineAsync(string family, IReadOnlyList<RunSummary> runs, CancellationToken ct);
}
