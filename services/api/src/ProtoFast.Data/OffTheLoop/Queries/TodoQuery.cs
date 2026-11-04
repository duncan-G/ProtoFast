using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop.Queries;

public sealed class TodoQuery : Query<Todo>, ITodoQuery
{
    public ITodoQuery WithId(Guid id)
    {
        Where(t => t.Id == id);
        return this;
    }

    public ITodoQuery OpenOrDoneSince(DateTime since)
    {
        Where(t => t.DoneAt == null || t.DoneAt >= since);
        return this;
    }

    public ITodoQuery InOrder()
    {
        Apply(q => q
            .OrderBy(t => t.DueOn == null)
            .ThenBy(t => t.DueOn)
            .ThenBy(t => t.DateCreated)
            .ThenBy(t => t.Id));
        return this;
    }
}
