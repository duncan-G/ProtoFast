using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="TimeOfDay"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ITimeOfDayRepository"/>.
/// </summary>
public interface ITimeOfDayQuery : IQuery<TimeOfDay>
{
    ITimeOfDayQuery WithId(Guid id);

    ITimeOfDayQuery InStory(Guid storyId);

    /// <summary>Case-insensitive.</summary>
    ITimeOfDayQuery WithLabel(string label);

    ITimeOfDayQuery InOrder();
}
