using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Queries;

public interface IApplicationQuery : IQuery<Application>
{
    IApplicationQuery WithId(Guid id);

    IApplicationQuery WithCompany();

    /// <summary>Open before closed, the furthest stage first, then the soonest next step.</summary>
    IApplicationQuery InLoopOrder();
}
