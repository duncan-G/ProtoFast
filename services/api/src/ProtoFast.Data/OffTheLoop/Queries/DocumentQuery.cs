using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop.Queries;

public sealed class DocumentQuery : Query<Document>, IDocumentQuery
{
    public IDocumentQuery WithId(Guid id)
    {
        Where(d => d.Id == id);
        return this;
    }

    public IDocumentQuery Uploaded()
    {
        Where(d => d.Uploaded);
        return this;
    }

    public IDocumentQuery NewestFirst()
    {
        Apply(q => q.OrderByDescending(d => d.DateCreated).ThenBy(d => d.Id));
        return this;
    }
}
