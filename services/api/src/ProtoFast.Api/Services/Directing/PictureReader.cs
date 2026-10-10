using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.Storage.Abstractions;
using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;

namespace ProtoFast.Api.Services.Directing;

/// <summary>Reads stored pictures for the director's model to look at.</summary>
public sealed class PictureReader(IObjectStore objectStore)
{
    /// <returns>Null when the bytes are gone from the store.</returns>
    public async Task<ModelImage?> ReadAsync(MediaAssetRecord asset, string caption, CancellationToken ct)
    {
        await using var stream = await objectStore.OpenReadAsync(asset.StorageKey, ct);
        if (stream is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, ct);
        return new ModelImage(bytes.ToArray(), asset.ContentType, caption, asset.StorageKey);
    }
}
