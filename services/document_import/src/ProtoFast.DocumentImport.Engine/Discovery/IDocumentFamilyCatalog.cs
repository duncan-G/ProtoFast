namespace ProtoFast.DocumentImport.Engine;

public interface IDocumentFamilyCatalog
{
    Task AddExecutorAsync(string family, ExecutorRef executor, CancellationToken ct);
    Task<IReadOnlyList<ExecutorRef>> ExecutorsAsync(string family, CancellationToken ct);
    Task AddVerifierAsync(string family, VerifierSpec spec, CancellationToken ct);
    Task<IReadOnlyList<VerifierSpec>> VerifiersAsync(string family, CancellationToken ct);
}
