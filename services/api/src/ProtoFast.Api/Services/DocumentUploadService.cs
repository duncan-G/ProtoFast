using Grpc.Core;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Storage;
using ProtoFast.Grpc;
using ProtoFast.Grpc.RateLimiting;
using ProtoFast.Storage.Abstractions;
using DocumentRecord = ProtoFast.Data.ThePlot.Entities.Document;
using DocumentUploadRecord = ProtoFast.Data.ThePlot.Entities.DocumentUpload;

namespace ProtoFast.Api.Services;

public class DocumentUploadService(
    IPresignedUrlFactory urls,
    IObjectStore objectStore,
    IUnitOfWorkFactory unitOfWorkFactory,
    IDocumentUploadRepository documentUploadRepository,
    IQueryFactory<DocumentUploadRecord, IDocumentUploadQuery> documentUploadQueries,
    IDocumentRepository documentRepository) : DocumentUpload.DocumentUploadBase
{
    // Each minted URL is a standing permission to write into the bucket, so the budget per caller
    // stays deliberately small.
    private const int UploadUrlWindowSeconds = 3600;
    private const int UploadUrlsPerWindow = 20;

    private const int MaxFileNameLength = 255;

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

        if (request.FileName.Length > MaxFileNameLength)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"The file name must be at most {MaxFileNameLength} characters."));
        }

        var uploadId = DocumentImportIds.New();
        var sourceKey = ArtifactKeys.UploadSource(caller.Subject, uploadId, format.Extension);
        var postPolicy = urls.PresignPost(sourceKey, format.MediaType, SourceFormats.DefaultMaxBytes);

        // Record the upload before handing out the URL, so every object that lands in the bucket
        // has a row to reconcile against. Signing is local, so nothing leaks if the insert fails.
        // The owner is stamped from the call's user context, never taken from the request.
        using (var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateDocumentUploadUrl)))
        {
            await documentUploadRepository.AddAsync(
                new DocumentUploadRecord
                {
                    UploadId = uploadId,
                    FileName = request.FileName,
                    SizeBytes = request.SizeBytes,
                    MediaType = format.MediaType,
                    FileExtension = format.Extension,
                },
                context.CancellationToken);

            await unitOfWork.CommitAsync(context.CancellationToken);
        }

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

    /// <summary>
    /// Turns a landed upload into a document. The browser posts straight to storage, so this is
    /// the only place the API learns the bytes arrived: it checks the object under the key the
    /// policy was signed for, then records the document under the same id. Nothing is done to the
    /// file beyond that.
    /// </summary>
    public override async Task<CompleteDocumentUploadReply> CompleteDocumentUpload(
        CompleteDocumentUploadRequest request,
        ServerCallContext context)
    {
        var caller = CallerIdentity.From(context);

        if (!DocumentImportIds.IsValid(request.UploadId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The upload id is not valid."));
        }

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CompleteDocumentUpload));

        // The upload query is owner-scoped, so another user's id reads as "no such upload".
        var upload = await documentUploadRepository.GetFirstByQueryAsync(
            documentUploadQueries.Create().WithUploadId(request.UploadId),
            context.CancellationToken);

        if (upload is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "No such upload."));
        }

        var existing = await documentRepository.GetByKeyAsync(upload.UploadId, context.CancellationToken);
        if (existing is not null)
        {
            return new CompleteDocumentUploadReply { Document = DocumentService.ToMessage(existing) };
        }

        var storageKey = ArtifactKeys.UploadSource(caller.Subject, upload.UploadId, upload.FileExtension);
        if (!await objectStore.ExistsAsync(storageKey, context.CancellationToken))
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "The file has not arrived in storage. Upload it, then complete the import."));
        }

        var document = new DocumentRecord
        {
            Id = upload.UploadId,
            Name = DocumentTitles.FromFileName(upload.FileName),
            FileName = upload.FileName,
            SizeBytes = upload.SizeBytes,
            MediaType = upload.MediaType,
            FileExtension = upload.FileExtension,
            StorageKey = storageKey,
        };

        await documentRepository.AddAsync(document, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new CompleteDocumentUploadReply { Document = DocumentService.ToMessage(document) };
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
