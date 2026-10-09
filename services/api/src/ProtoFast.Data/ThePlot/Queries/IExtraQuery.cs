using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Extra"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IExtraRepository"/>.
/// </summary>
public interface IExtraQuery : IQuery<Extra>
{
    IExtraQuery WithId(Guid id);

    IExtraQuery InStory(Guid storyId);
}
