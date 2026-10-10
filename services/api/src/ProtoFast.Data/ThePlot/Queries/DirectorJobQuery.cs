using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class DirectorJobQuery : Query<DirectorJob>, IDirectorJobQuery
{
    public IDirectorJobQuery WithId(Guid id)
    {
        Where(j => j.Id == id);
        return this;
    }

    public IDirectorJobQuery WithIds(IReadOnlyCollection<Guid> ids)
    {
        Where(j => ids.Contains(j.Id));
        return this;
    }

    public IDirectorJobQuery WithStatus(params DirectorJobStatus[] statuses)
    {
        Where(j => statuses.Contains(j.Status));
        return this;
    }

    public IDirectorJobQuery InOrder()
    {
        Apply(q => q.OrderBy(j => j.DateCreated).ThenBy(j => j.Id));
        return this;
    }
}
