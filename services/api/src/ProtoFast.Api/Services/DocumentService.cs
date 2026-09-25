using Grpc.Core;
using ProtoFast.Data.ThePlot.Queries;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using DocumentRecord = ProtoFast.Data.ThePlot.Entities.Document;

namespace ProtoFast.Api.Services;

/// <summary>
/// The caller's documents. Every query runs under the user context the gRPC interceptor set, so
/// the repository only ever sees the caller's own rows.
/// </summary>
public class DocumentService(
    IUnitOfWorkFactory unitOfWorkFactory,
    IDocumentRepository documentRepository,
    IQueryFactory<DocumentRecord, IDocumentQuery> documentQueries) : Documents.DocumentsBase
{
    public override async Task<ListDocumentsReply> ListDocuments(
        ListDocumentsRequest request,
        ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListDocuments));

        var documents = await documentRepository.GetByQueryAsync(
            documentQueries.Create().NewestFirst(),
            context.CancellationToken);

        var reply = new ListDocumentsReply();
        reply.Documents.AddRange(documents.Select(ToMessage));
        return reply;
    }

    internal static Document ToMessage(DocumentRecord document) => new()
    {
        Id = document.Id,
        Name = document.Name,
        FileName = document.FileName,
        SizeBytes = document.SizeBytes,
        MediaType = document.MediaType,
        FileExtension = document.FileExtension,
        CreatedUnixSeconds = new DateTimeOffset(document.DateCreated, TimeSpan.Zero).ToUnixTimeSeconds(),
        LastModifiedUnixSeconds = new DateTimeOffset(document.DateLastModified, TimeSpan.Zero).ToUnixTimeSeconds(),
    };
}
