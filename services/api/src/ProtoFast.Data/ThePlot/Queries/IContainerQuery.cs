using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Container"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IContainerRepository"/>.
/// </summary>
public interface IContainerQuery : IQuery<Container>
{
    IContainerQuery WithId(Guid id);

    IContainerQuery InStory(Guid storyId);

    IContainerQuery AtOrAfterPosition(int position);

    IContainerQuery InOrder();
}
