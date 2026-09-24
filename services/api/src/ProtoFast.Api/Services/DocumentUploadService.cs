using Grpc.Core;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Storage;
using ProtoFast.Grpc;
using ProtoFast.Grpc.RateLimiting;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Api.Services;

public class DocumentUploadService(IPresignedUrlFactory urls) : DocumentUpload.DocumentUploadBase
{
    // Each minted URL is a standing permission to write into the bucket, so the budget per caller
    // stays deliberately small.
    private const int UploadUrlWindowSeconds = 3600;
    private const int UploadUrlsPerWindow = 20;

    public override async Task<CreateDocumentUploadUrlReply> CreateDocumentUploadUrl(
        CreateDocumentUploadUrlRequest request,
        ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);

        await context
            .EnforceFixedLimitByIdentityAsync(UploadUrlWindowSeconds, UploadUrlsPerWindow, context.CancellationToken)
            .ConfigureAwait(false);

        if (!SourceFormats.TryResolve(request.FileName, request.ContentType, out var format))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"Unsupported document type: {SourceFormats.DescribeRejected(request.FileName)}."));
        }

        if (request.SizeBytes is <= 0 or > SourceFormats.DefaultMaxBytes)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"The document must be between 1 byte and {SourceFormats.DefaultMaxBytes / (1024 * 1024)} MB."));
        }

        var uploadId = DocumentImportIds.New();

        var sourceKey = ArtifactKeys.UploadSource(caller.Subject, uploadId, format.Extension);

        var postPolicy = urls.PresignPost(sourceKey, format.MediaType, SourceFormats.DefaultMaxBytes);

        var reply = new CreateDocumentUploadUrlReply
        {
            UploadId = uploadId,
            PostUrl = postPolicy.PostUrl,
            MaxBytes = postPolicy.MaxBytes,
            ExpiresUnixSeconds = postPolicy.ExpiresAt.ToUnixTimeSeconds(),
        };

        // Posted back verbatim, with the file part last: S3 stops reading at the file, so a field
        // after it is never seen and the upload fails the policy it was signed against.
        foreach (var (name, value) in postPolicy.Fields)
        {
            reply.Fields.Add(name, value);
        }

        return reply;
    }

    public override Task<ListSourceFormatsReply> ListSourceFormats(
        ListSourceFormatsRequest request, ServerCallContext context)
    {
        var reply = new ListSourceFormatsReply { MaxBytes = SourceFormats.DefaultMaxBytes };

        reply.Formats.AddRange(SourceFormats.All.Select(f => new SourceFormat
        {
            Extension = f.Extension,
            MediaType = f.MediaType,
            Label = f.Label,
            ProducesLayout = f.ProducesLayout,
            OcrCapable = f.OcrCapable,
        }));

        return Task.FromResult(reply);
    }
}
