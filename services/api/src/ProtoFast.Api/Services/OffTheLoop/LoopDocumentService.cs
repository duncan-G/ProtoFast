using Grpc.Core;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.Grpc.RateLimiting;
using ProtoFast.Storage.Abstractions;
using DocumentRecord = ProtoFast.Data.OffTheLoop.Entities.Document;
using Loop = ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// The caller's resumes and CVs. Files live in the documents bucket under
/// <c>offtheloop/{user}/documents/</c>; a row is recorded before its URL is handed out, so every
/// object that lands has one to reconcile against.
/// </summary>
public class LoopDocumentService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    IDocumentRepository documentRepository,
    IQueryFactory<DocumentRecord, IDocumentQuery> documentQueries,
    UserContext userContext,
    IPresignedUrlFactory urls,
    IObjectStore objectStore,
    ILogger<LoopDocumentService> logger) : Loop.Documents.DocumentsBase
{
    // Each minted URL is a standing permission to write into the bucket.
    private const int UploadUrlWindowSeconds = 3600;
    private const int UploadUrlsPerWindow = 20;

    private static readonly TimeSpan OpenLinkTtl = TimeSpan.FromMinutes(5);

    public override async Task<Loop.ListDocumentsReply> ListDocuments(
        Loop.ListDocumentsRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListDocuments));
        var documents = await documentRepository.GetByQueryAsync(
            documentQueries.Create().Uploaded().NewestFirst(), context.CancellationToken);

        var reply = new Loop.ListDocumentsReply();
        reply.Documents.AddRange(documents.Select(DocumentMessages.ToMessage));
        return reply;
    }

    public override async Task<Loop.CreateDocumentUploadReply> CreateDocumentUpload(
        Loop.CreateDocumentUploadRequest request, ServerCallContext context)
    {
        var kind = DocumentMessages.ToRecord(request.Kind);
        var fileName = LoopText.Required(
            request.FileName, OffTheLoopLimits.FileName, "The file’s name", "The file has no name.");
        var format = DocumentFormats.Of(fileName);
        if (request.SizeBytes is <= 0 or > DocumentFormats.MaxBytes)
        {
            throw LoopErrors.Invalid($"The file must be under {DocumentFormats.MaxBytes / (1024 * 1024)} MB.");
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? DocumentMessages.NameFrom(fileName)
            : DocumentMessages.Name(request.Name);

        await context
            .EnforceFixedLimitByIdentityAsync(UploadUrlWindowSeconds, UploadUrlsPerWindow, context.CancellationToken)
            .ConfigureAwait(false);

        var userId = userContext.CurrentUserId
                     ?? throw new InvalidOperationException("A document upload needs the caller's user id.");
        var documentId = Guid.CreateVersion7();
        var document = new DocumentRecord
        {
            Id = documentId,
            Kind = kind,
            Name = name,
            FileName = fileName,
            MediaType = format.MediaType,
            SizeBytes = request.SizeBytes,
            StorageKey = $"offtheloop/{userId}/documents/{documentId}{format.Extension}",
        };
        var post = urls.PresignPost(document.StorageKey, format.MediaType, DocumentFormats.MaxBytes);

        using (var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateDocumentUpload)))
        {
            await documentRepository.AddAsync(document, context.CancellationToken);
            await unitOfWork.CommitAsync(context.CancellationToken);
        }

        var reply = new Loop.CreateDocumentUploadReply
        {
            DocumentId = documentId.ToString(),
            PostUrl = post.PostUrl,
            MaxBytes = post.MaxBytes,
        };
        foreach (var (field, value) in post.Fields)
        {
            reply.Fields.Add(field, value);
        }

        return reply;
    }

    public override async Task<Loop.CompleteDocumentUploadReply> CompleteDocumentUpload(
        Loop.CompleteDocumentUploadRequest request, ServerCallContext context)
    {
        var documentId = LoopIds.Existing(request.DocumentId, LoopErrors.DocumentNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CompleteDocumentUpload));
        var document = await documentRepository.GetFirstByQueryAsync(
                           documentQueries.Create().WithId(documentId), context.CancellationToken)
                       ?? throw LoopErrors.NotFound(LoopErrors.DocumentNotFound);
        if (!document.Uploaded)
        {
            if (!await objectStore.ExistsAsync(document.StorageKey, context.CancellationToken))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, LoopErrors.DocumentNotArrived));
            }

            document.Uploaded = true;
            await unitOfWork.CommitAsync(context.CancellationToken);
        }

        return new Loop.CompleteDocumentUploadReply { Document = DocumentMessages.ToMessage(document) };
    }

    public override async Task<Loop.RenameDocumentReply> RenameDocument(
        Loop.RenameDocumentRequest request, ServerCallContext context)
    {
        var documentId = LoopIds.Existing(request.DocumentId, LoopErrors.DocumentNotFound);
        var name = DocumentMessages.Name(request.Name);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(RenameDocument));
        var document = await RequireUploadedAsync(documentId, context.CancellationToken);
        document.Name = name;
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new Loop.RenameDocumentReply { Document = DocumentMessages.ToMessage(document) };
    }

    public override async Task<Loop.OpenDocumentReply> OpenDocument(Loop.OpenDocumentRequest request, ServerCallContext context)
    {
        var documentId = LoopIds.Existing(request.DocumentId, LoopErrors.DocumentNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(OpenDocument));
        var document = await RequireUploadedAsync(documentId, context.CancellationToken);

        return new Loop.OpenDocumentReply { Url = urls.PresignGet(document.StorageKey, OpenLinkTtl, document.FileName) };
    }

    /// <summary>Applications let go of it in the database; the file goes once the row has.</summary>
    public override async Task<Loop.DeleteDocumentReply> DeleteDocument(
        Loop.DeleteDocumentRequest request, ServerCallContext context)
    {
        var documentId = LoopIds.Existing(request.DocumentId, LoopErrors.DocumentNotFound);
        string storageKey;
        using (var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteDocument)))
        {
            var document = await documentRepository.GetFirstByQueryAsync(
                               documentQueries.Create().WithId(documentId), context.CancellationToken)
                           ?? throw LoopErrors.NotFound(LoopErrors.DocumentNotFound);
            storageKey = document.StorageKey;
            await documentRepository.RemoveAsync(document, context.CancellationToken);
            await unitOfWork.CommitAsync(context.CancellationToken);
        }

        try
        {
            await objectStore.DeleteAsync(storageKey, context.CancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Document {DocumentId} was deleted but its file {StorageKey} was not", documentId, storageKey);
        }

        return new Loop.DeleteDocumentReply();
    }

    private async Task<DocumentRecord> RequireUploadedAsync(Guid documentId, CancellationToken cancellationToken) =>
        await documentRepository.GetFirstByQueryAsync(
            documentQueries.Create().WithId(documentId).Uploaded(), cancellationToken)
        ?? throw LoopErrors.NotFound(LoopErrors.DocumentNotFound);
}
