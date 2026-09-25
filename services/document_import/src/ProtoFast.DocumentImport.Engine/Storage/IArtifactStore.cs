namespace ProtoFast.DocumentImport.Engine;

public interface IArtifactStore
{
    Task<ArtifactRef> PutAsync(string runId, string stageId, Stream content, ContractRef contract, CancellationToken ct);
    Task<Stream> GetAsync(ArtifactRef reference, CancellationToken ct);

    Task<ContractRef> ContractOfAsync(ArtifactRef reference, CancellationToken ct);
}
