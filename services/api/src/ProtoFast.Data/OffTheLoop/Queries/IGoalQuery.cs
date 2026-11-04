using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Queries;

public interface IGoalQuery : IQuery<Goal>
{
    IGoalQuery WithId(Guid id);

    /// <summary>Oldest first.</summary>
    IGoalQuery InOrder();
}
