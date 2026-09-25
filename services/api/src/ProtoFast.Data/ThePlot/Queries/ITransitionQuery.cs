using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Transition"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ITransitionRepository"/>.
/// </summary>
public interface ITransitionQuery : IQuery<Transition>
{
    ITransitionQuery WithId(Guid id);

    ITransitionQuery InStory(Guid storyId);

    /// <summary>Case-insensitive.</summary>
    ITransitionQuery WithLabel(string label);

    ITransitionQuery InOrder();
}
