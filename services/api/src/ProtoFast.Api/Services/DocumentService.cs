using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Storage;
using DocumentRecord = ProtoFast.Data.ThePlot.Entities.Document;
using DocumentUploadRecord = ProtoFast.Data.ThePlot.Entities.DocumentUpload;

namespace ProtoFast.Api.Services;

/// <summary>
/// The caller's documents. Every query runs under the user context the gRPC interceptor set, so
/// the repository only ever sees the caller's own rows. The run ledger is not owner-scoped, so it
/// is only ever asked about ids those rows vouch for.
/// </summary>
public class DocumentService(
    IUnitOfWorkFactory unitOfWorkFactory,
    IDocumentRepository documentRepository,
    IQueryFactory<DocumentRecord, IDocumentQuery> documentQueries,
    IDocumentUploadRepository documentUploadRepository,
    IQueryFactory<DocumentUploadRecord, IDocumentUploadQuery> documentUploadQueries,
    IRunLedger ledger) : Documents.DocumentsBase
{
    private const int MaxProgressIds = 50;

    public override async Task<ListDocumentsReply> ListDocuments(
        ListDocumentsRequest request,
        ServerCallContext context)
    {
        IReadOnlyList<DocumentRecord> documents;
        using (unitOfWorkFactory.CreateReadOnly(nameof(ListDocuments)))
        {
            documents = await documentRepository.GetByQueryAsync(
                documentQueries.Create().NewestFirst(),
                context.CancellationToken);
        }

        var progress = await ledger.ProgressAsync(documents.Select(d => d.Id).ToList(), context.CancellationToken);

        var reply = new ListDocumentsReply();
        reply.Documents.AddRange(documents.Select(d => ToMessage(d, progress.GetValueOrDefault(d.Id))));
        return reply;
    }

    public override async Task<GetImportProgressReply> GetImportProgress(
        GetImportProgressRequest request,
        ServerCallContext context)
    {
        var ids = request.UploadIds.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count > MaxProgressIds)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument, $"Ask about at most {MaxProgressIds} uploads at a time."));
        }

        if (ids.Any(id => !DocumentImportIds.IsValid(id)))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "An upload id is not valid."));
        }

        var reply = new GetImportProgressReply();
        if (ids.Count == 0)
        {
            return reply;
        }

        List<string> owned;
        HashSet<string> onDesk;
        using (unitOfWorkFactory.CreateReadOnly(nameof(GetImportProgress)))
        {
            var uploads = await documentUploadRepository.GetByQueryAsync(
                documentUploadQueries.Create().WithUploadIds(ids), context.CancellationToken);
            owned = uploads.Select(u => u.UploadId).ToList();

            var documents = await documentRepository.GetByQueryAsync(
                documentQueries.Create().WithIds(owned), context.CancellationToken);
            onDesk = documents.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        }

        var progress = await ledger.ProgressAsync(owned, context.CancellationToken);
        foreach (var id in owned)
        {
            var found = progress.GetValueOrDefault(id);
            if (found is not null || onDesk.Contains(id))
            {
                reply.Imports.Add(ImportProgressMessages.From(id, found, onDesk.Contains(id)));
            }
        }

        return reply;
    }

    /// <summary>
    /// The document goes first: once it is gone the worker can no longer save a story from it, and
    /// the ledger's cancel only stops the run sooner. A story saved first wins.
    /// </summary>
    public override async Task<CancelImportReply> CancelImport(CancelImportRequest request, ServerCallContext context)
    {
        if (!DocumentImportIds.IsValid(request.UploadId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The upload id is not valid."));
        }

        using (var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CancelImport)))
        {
            var upload = await documentUploadRepository.GetFirstByQueryAsync(
                documentUploadQueries.Create().WithUploadId(request.UploadId), context.CancellationToken);
            if (upload is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "No such upload."));
            }

            if (await documentRepository.GetByKeyAsync(upload.UploadId, context.CancellationToken) is { } document)
            {
                await documentRepository.RemoveAsync(document, context.CancellationToken);
                try
                {
                    await unitOfWork.CommitAsync(context.CancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw AlreadyAStory();
                }
            }
        }

        await ledger.ReportAsync(request.UploadId, new RunProgress(RunPhase.Cancelled), context.CancellationToken);
        var progress = (await ledger.ProgressAsync([request.UploadId], context.CancellationToken))[request.UploadId];
        if (progress.Phase == RunPhase.Finished)
        {
            throw AlreadyAStory();
        }

        return new CancelImportReply { Import = ImportProgressMessages.From(request.UploadId, progress, documentExists: false) };
    }

    private static RpcException AlreadyAStory() =>
        new(new Status(StatusCode.FailedPrecondition, "This import has already finished; its story is on your desk."));

    internal static Document ToMessage(DocumentRecord document, RunProgress? progress) => new()
    {
        Id = document.Id,
        Name = document.Name,
        FileName = document.FileName,
        SizeBytes = document.SizeBytes,
        MediaType = document.MediaType,
        FileExtension = document.FileExtension,
        CreatedUnixSeconds = new DateTimeOffset(document.DateCreated, TimeSpan.Zero).ToUnixTimeSeconds(),
        LastModifiedUnixSeconds = new DateTimeOffset(document.DateLastModified, TimeSpan.Zero).ToUnixTimeSeconds(),
        Import = ImportProgressMessages.From(document.Id, progress, documentExists: true),
    };
}
