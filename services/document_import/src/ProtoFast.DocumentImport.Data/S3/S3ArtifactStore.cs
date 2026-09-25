using System.Security.Cryptography;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Storage;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Data.S3;

/// <summary>Artifacts are frozen; the contract each was written against sits beside it.</summary>
public sealed class S3ArtifactStore(IObjectStore objects) : IArtifactStore
{
    public async Task<ArtifactRef> PutAsync(
        string runId, string stageId, Stream content, ContractRef contract, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        // The contract goes first, so an artifact that exists always has one.
        var contractKey = ArtifactKeys.RunArtifactContract(runId, stageId, hash);
        if (!await objects.ExistsAsync(contractKey, ct))
        {
            await objects.WriteFrozenAsync(contractKey, contract, hash, ct);
        }

        var key = ArtifactKeys.RunArtifact(runId, stageId, hash);
        if (!await objects.ExistsAsync(key, ct))
        {
            await objects.WriteFrozenBytesAsync(key, bytes, "application/octet-stream", hash, ct);
        }

        return new ArtifactRef(runId, stageId, hash);
    }

    public async Task<Stream> GetAsync(ArtifactRef reference, CancellationToken ct) =>
        await objects.OpenReadAsync(ArtifactKeys.RunArtifact(reference.RunId, reference.StageId, reference.Hash), ct)
        ?? throw new KeyNotFoundException($"Artifact {reference} does not exist.");

    public async Task<ContractRef> ContractOfAsync(ArtifactRef reference, CancellationToken ct) =>
        await objects.ReadAsync<ContractRef?>(
            ArtifactKeys.RunArtifactContract(reference.RunId, reference.StageId, reference.Hash), ct)
        ?? throw new KeyNotFoundException($"Artifact {reference} does not exist.");
}
