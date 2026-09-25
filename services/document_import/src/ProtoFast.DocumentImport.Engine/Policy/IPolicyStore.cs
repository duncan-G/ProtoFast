namespace ProtoFast.DocumentImport.Engine;

public interface IPolicyStore
{
    // A stage with no row gets PolicyRow.Default.
    Task<IReadOnlyDictionary<string, PolicyRow>> SnapshotAsync(
        string family, IEnumerable<string> stageIds, CancellationToken ct);
    Task<PolicyRow> GetAsync(string family, string stageId, CancellationToken ct);
    Task PutAsync(PolicyRow row, CancellationToken ct);
}
