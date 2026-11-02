using System.Text;
using ProtoFast.DocumentImport.Core;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>
/// The text a run starts from: the upload itself for Markdown and plain text, the conversion
/// worker's Markdown for everything else. An upload that needs converting is sent to the worker,
/// which sends it back with the outcome; one the worker refused is unreadable.
/// </summary>
public sealed class SourceTextResolver(
    IObjectStore objects,
    ConversionClient conversion)
{
    /// <summary>Null when the upload has gone to the conversion worker.</summary>
    public async Task<string?> ResolveAsync(DocumentImportRequested request, CancellationToken ct)
    {
        var requiresConversion = !SourceFormats.TryResolve(request.FileName, request.MediaType, out var format)
                                 || format.RequiresConversion;
        if (!requiresConversion)
        {
            return await ReadTextAsync(request.StorageKey, ct);
        }

        switch (request.Conversion)
        {
            case null:
                await conversion.RequestAsync(request, ct);
                return null;
            case { Error: { } error }:
                throw new UnreadableSourceException(request.StorageKey, $"conversion refused it ({error})");
            case { MarkdownKey: { } markdownKey }:
                return await ReadTextAsync(markdownKey, ct);
            default:
                throw new UnreadableSourceException(request.StorageKey, "conversion sent back no Markdown");
        }
    }

    private async Task<string> ReadTextAsync(string key, CancellationToken ct)
    {
        await using var stream = await objects.OpenReadAsync(key, ct)
            ?? throw new FileNotFoundException($"Object {key} is not in the bucket.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return SourceTextNormalizer.Normalize(await reader.ReadToEndAsync(ct));
    }
}
