using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Act"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IActRepository"/>.
/// </summary>
public interface IActQuery : IQuery<Act>
{
    IActQuery WithId(Guid id);

    IActQuery InStory(Guid storyId);

    IActQuery AtOrAfterPosition(int position);

    IActQuery InOrder();
}
