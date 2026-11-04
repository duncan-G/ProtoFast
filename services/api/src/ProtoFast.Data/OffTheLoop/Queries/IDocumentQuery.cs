using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Queries;

public interface IDocumentQuery : IQuery<Document>
{
    IDocumentQuery WithId(Guid id);

    IDocumentQuery Uploaded();

    IDocumentQuery NewestFirst();
}
