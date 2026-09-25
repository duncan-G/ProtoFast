using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class DocumentQuery : Query<Document>, IDocumentQuery
{
    public IDocumentQuery WithId(string id)
    {
        Where(d => d.Id == id);
        return this;
    }

    public IDocumentQuery WithName(string name)
    {
        Where(d => d.Name == name);
        return this;
    }

    public IDocumentQuery NewestFirst()
    {
        Apply(q => q.OrderByDescending(d => d.DateCreated).ThenByDescending(d => d.Id));
        return this;
    }
}
