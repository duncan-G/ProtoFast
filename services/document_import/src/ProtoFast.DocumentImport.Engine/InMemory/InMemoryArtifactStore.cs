using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ProtoFast.DocumentImport.Engine.InMemory;

public sealed class InMemoryArtifactStore : IArtifactStore
{
    private readonly ConcurrentDictionary<ArtifactRef, (byte[] Content, ContractRef Contract)> _artifacts = new();

    public async Task<ArtifactRef> PutAsync(
        string runId, string stageId, Stream content, ContractRef contract, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        var reference = new ArtifactRef(runId, stageId, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        _artifacts.TryAdd(reference, (bytes, contract));
        return reference;
    }

    public Task<Stream> GetAsync(ArtifactRef reference, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream(Find(reference).Content, writable: false));

    public Task<ContractRef> ContractOfAsync(ArtifactRef reference, CancellationToken ct) =>
        Task.FromResult(Find(reference).Contract);

    private (byte[] Content, ContractRef Contract) Find(ArtifactRef reference) =>
        _artifacts.TryGetValue(reference, out var artifact)
            ? artifact
            : throw new KeyNotFoundException($"Artifact {reference} does not exist.");
}
