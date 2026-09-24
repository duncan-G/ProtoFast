using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class DocumentQuery : Query<Document>, IDocumentQuery
{
    public IDocumentQuery WithId(Guid id)
    {
        Where(d => d.Id == id);
        return this;
    }

    public IDocumentQuery WithName(string name)
    {
        Where(d => d.Name == name);
        return this;
    }
}
