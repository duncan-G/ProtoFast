using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Queries;

public interface ITodoQuery : IQuery<Todo>
{
    ITodoQuery WithId(Guid id);

    ITodoQuery OpenOrDoneSince(DateTime since);

    /// <summary>Soonest due first, undated last, then oldest first.</summary>
    ITodoQuery InOrder();
}
