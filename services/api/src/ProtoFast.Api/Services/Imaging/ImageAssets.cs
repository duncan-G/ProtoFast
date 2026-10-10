using System.Security.Cryptography;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.Storage.Abstractions;
using MediaAssetKindRecord = ProtoFast.Data.ThePlot.Entities.MediaAssetKind;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;
using MediaReferenceRecord = ProtoFast.Data.ThePlot.Entities.MediaReference;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>The media cache for pictures: one asset per story, kind and request hash, its bytes in the object store.</summary>
public sealed class ImageAssets(
    IImageGenerator generator,
    IObjectStore objectStore,
    IMediaAssetRepository assets,
    IQueryFactory<MediaAssetRecord, IMediaAssetQuery> assetQueries)
{
    public Task<MediaAssetRecord?> FindAsync(Guid storyId, MediaAssetKindRecord kind, string requestHash, CancellationToken ct) =>
        assets.GetFirstByQueryAsync(assetQueries.Create().InStory(storyId).OfKind(kind).WithRequestHash(requestHash), ct);

    public Task<MediaAssetRecord?> GetAsync(Guid storyId, Guid assetId, CancellationToken ct) =>
        assets.GetFirstByQueryAsync(assetQueries.Create().InStory(storyId).WithId(assetId), ct);

    public async Task<ImageInput?> ReadAsync(MediaAssetRecord asset, string caption, CancellationToken ct)
    {
        await using var stream = await objectStore.OpenReadAsync(asset.StorageKey, ct);
        if (stream is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, ct);
        return new ImageInput(bytes.ToArray(), asset.ContentType, caption, asset.StorageKey);
    }

    /// <summary>Stores the picture; an asset for the same request is rendered again in place, so whatever points at it follows.</summary>
    public async Task<MediaAssetRecord> StoreAsync(
        Guid storyId,
        MediaAssetKindRecord kind,
        string requestHash,
        ImageRequest request,
        GeneratedImage image,
        List<MediaReferenceRecord> references,
        CancellationToken ct)
    {
        var (key, contentHash) = await WriteAsync(storyId, image, requestHash, ct);
        var asset = await FindAsync(storyId, kind, requestHash, ct);
        if (asset is null)
        {
            asset = new MediaAssetRecord
            {
                StoryId = storyId,
                Kind = kind,
                StorageKey = key,
                ContentType = image.ContentType,
                ContentHash = contentHash,
                RequestHash = requestHash,
                Provider = generator.Provider,
                Model = generator.Model,
            };
            await assets.AddAsync(asset, ct);
        }
        else
        {
            asset.StorageKey = key;
            asset.ContentType = image.ContentType;
            asset.ContentHash = contentHash;
        }

        asset.ByteSize = image.Bytes.Length;
        asset.CostUsdMicros += image.CostUsdMicros;
        asset.Prompt = request.Transcript();
        asset.References = references;
        asset.Notes = image.Notes;
        return asset;
    }

    /// <summary>
    /// Puts an edit of the picture in its place. The request hash stays, so the picture is still current
    /// for what it pictures; the edit is added to its prompt for the record.
    /// </summary>
    public async Task ReplaceAsync(MediaAssetRecord asset, ImageRequest request, GeneratedImage image, CancellationToken ct)
    {
        (asset.StorageKey, asset.ContentHash) = await WriteAsync(asset.StoryId, image, asset.RequestHash, ct);
        asset.ContentType = image.ContentType;
        asset.ByteSize = image.Bytes.Length;
        asset.CostUsdMicros += image.CostUsdMicros;
        asset.Prompt = string.Join("\n\n", new[] { asset.Prompt ?? "", request.Transcript() }.Where(t => t.Length > 0));
        asset.Notes = image.Notes;
    }

    private async Task<(string Key, string ContentHash)> WriteAsync(Guid storyId, GeneratedImage image, string requestHash, CancellationToken ct)
    {
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(image.Bytes));
        var key = $"stories/{storyId}/images/{contentHash}.{ImageRequests.Extension(image.ContentType)}";
        await objectStore.WriteBytesAsync(key, image.Bytes, image.ContentType, requestHash, ct);
        return (key, contentHash);
    }
}
