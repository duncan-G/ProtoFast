using ProtoFast.DocumentImport.Core;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Worker.Import;

public sealed class ConversionClient([FromKeyedServices(DocumentImportQueues.ConversionQueueKey)] IMessageQueue queue)
{
    public Task RequestAsync(DocumentImportRequested request, CancellationToken ct) =>
        queue.SendAsync(
            new ConversionRequested(
                request.UploadId,
                request.StorageKey,
                WithExtension(request.StorageKey, ".md"),
                WithExtension(request.StorageKey, ".layout.json"),
                WithExtension(request.StorageKey, ".conversion.json"),
                request.MediaType,
                request),
            groupId: request.UserId,
            ct);

    private static string WithExtension(string key, string extension)
    {
        var dot = key.LastIndexOf('.');
        var slash = key.LastIndexOf('/');
        return (dot > slash ? key[..dot] : key) + extension;
    }
}
