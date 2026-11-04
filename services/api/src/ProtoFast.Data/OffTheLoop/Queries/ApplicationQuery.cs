using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop.Queries;

public sealed class ApplicationQuery : Query<Application>, IApplicationQuery
{
    public IApplicationQuery WithId(Guid id)
    {
        Where(a => a.Id == id);
        return this;
    }

    public IApplicationQuery WithCompany()
    {
        Apply(q => q.Include(a => a.Company));
        return this;
    }

    public IApplicationQuery InLoopOrder()
    {
        // Stages are stored by name, so rank them here rather than sort the text.
        Apply(q => q
            .OrderBy(a => a.Closed)
            .ThenByDescending(a =>
                a.Stage == ApplicationStage.Offer ? 4 :
                a.Stage == ApplicationStage.Onsite ? 3 :
                a.Stage == ApplicationStage.Tech ? 2 :
                a.Stage == ApplicationStage.Screen ? 1 : 0)
            .ThenBy(a => a.NextOn == null)
            .ThenBy(a => a.NextOn)
            .ThenBy(a => a.AppliedOn == null)
            .ThenByDescending(a => a.AppliedOn)
            .ThenBy(a => a.Id));
        return this;
    }
}
