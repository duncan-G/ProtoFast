using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="DocumentUpload"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IDocumentUploadRepository"/>.
/// </summary>
public interface IDocumentUploadQuery : IQuery<DocumentUpload>
{
    IDocumentUploadQuery WithUploadId(string uploadId);

    IDocumentUploadQuery WithFileName(string fileName);

    IDocumentUploadQuery WithMediaType(string mediaType);

    IDocumentUploadQuery CreatedSince(DateTime dateCreatedUtc);
}
