using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop.Queries;

public sealed class GoalQuery : Query<Goal>, IGoalQuery
{
    public IGoalQuery WithId(Guid id)
    {
        Where(g => g.Id == id);
        return this;
    }

    public IGoalQuery InOrder()
    {
        Apply(q => q.OrderBy(g => g.DateCreated).ThenBy(g => g.Id));
        return this;
    }
}
