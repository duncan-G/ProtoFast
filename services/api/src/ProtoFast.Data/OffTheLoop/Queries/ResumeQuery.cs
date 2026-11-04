using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop.Queries;

public sealed class ResumeQuery : Query<Resume>, IResumeQuery
{
    public IResumeQuery WithId(Guid id)
    {
        Where(r => r.Id == id);
        return this;
    }

    public IResumeQuery ForApplication(Guid? applicationId)
    {
        Where(r => r.ApplicationId == applicationId);
        return this;
    }

    public IResumeQuery InOrder()
    {
        Apply(q => q.OrderBy(r => r.ApplicationId != null).ThenByDescending(r => r.DateLastModified).ThenBy(r => r.Id));
        return this;
    }
}
