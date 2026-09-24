using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Document"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IDocumentRepository"/>.
/// </summary>
public interface IDocumentQuery : IQuery<Document>
{
    IDocumentQuery WithId(Guid id);

    IDocumentQuery WithName(string name);
}
