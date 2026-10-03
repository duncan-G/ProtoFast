using System.Text;
using ProtoFast.DocumentImport.Core;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>
/// The text a run starts from: the upload itself for Markdown and plain text, the conversion
/// service's Markdown for everything else. A format that needs conversion is unreadable when the
/// conversion service is missing or rejects it; network errors are left to redelivery.
/// </summary>
public sealed class SourceTextResolver(
    IObjectStore objects,
    ConversionClient conversion)
{
    public async Task<string> ResolveAsync(DocumentImportRequested request, CancellationToken ct)
    {
        var requiresConversion = !SourceFormats.TryResolve(request.FileName, request.MediaType, out var format)
                                 || format.RequiresConversion;

        if (requiresConversion)
        {
            if (!conversion.IsConfigured)
            {
                throw new UnreadableSourceException(request.StorageKey, "it needs conversion and no conversion service is configured");
            }

            try
            {
                var markdownKey = await conversion.ConvertAsync(new ConvertRequest(
                    request.UploadId,
                    request.StorageKey,
                    WithExtension(request.StorageKey, ".md"),
                    WithExtension(request.StorageKey, ".layout.json"),
                    WithExtension(request.StorageKey, ".conversion.json"),
                    request.MediaType), ct);
                return await ReadTextAsync(markdownKey, ct);
            }
            catch (HttpRequestException e) when (e.StatusCode is not null)
            {
                throw new UnreadableSourceException(request.StorageKey, $"conversion returned {(int)e.StatusCode}", e);
            }
        }

        return await ReadTextAsync(request.StorageKey, ct);
    }

    private async Task<string> ReadTextAsync(string key, CancellationToken ct)
    {
        await using var stream = await objects.OpenReadAsync(key, ct)
            ?? throw new FileNotFoundException($"Object {key} is not in the bucket.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    private static string WithExtension(string key, string extension)
    {
        var dot = key.LastIndexOf('.');
        var slash = key.LastIndexOf('/');
        return (dot > slash ? key[..dot] : key) + extension;
    }
}
