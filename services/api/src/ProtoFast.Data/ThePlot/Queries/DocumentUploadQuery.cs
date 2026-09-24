using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class DocumentUploadQuery : Query<DocumentUpload>, IDocumentUploadQuery
{
    public IDocumentUploadQuery WithUploadId(string uploadId)
    {
        Where(u => u.UploadId == uploadId);
        return this;
    }

    public IDocumentUploadQuery WithFileName(string fileName)
    {
        Where(u => u.FileName == fileName);
        return this;
    }

    public IDocumentUploadQuery WithMediaType(string mediaType)
    {
        Where(u => u.MediaType == mediaType);
        return this;
    }

    public IDocumentUploadQuery CreatedSince(DateTime dateCreatedUtc)
    {
        Where(u => u.DateCreated >= dateCreatedUtc);
        return this;
    }
}
