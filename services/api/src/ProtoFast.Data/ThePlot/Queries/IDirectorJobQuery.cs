using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="DirectorJob"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IDirectorJobRepository"/>.
/// </summary>
public interface IDirectorJobQuery : IQuery<DirectorJob>
{
    IDirectorJobQuery WithId(Guid id);

    IDirectorJobQuery WithIds(IReadOnlyCollection<Guid> ids);

    IDirectorJobQuery WithStatus(params DirectorJobStatus[] statuses);

    IDirectorJobQuery InOrder();
}
