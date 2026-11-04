using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop.Queries;

public sealed class ModelRunQuery : Query<ModelRun>, IModelRunQuery
{
    public IModelRunQuery WithId(Guid id)
    {
        Where(r => r.Id == id);
        return this;
    }

    public IModelRunQuery WithIds(IReadOnlyCollection<Guid> ids)
    {
        Where(r => ids.Contains(r.Id));
        return this;
    }

    public IModelRunQuery WithStatus(params ModelRunStatus[] statuses)
    {
        Where(r => statuses.Contains(r.Status));
        return this;
    }

    public IModelRunQuery OnSubject(ModelRunKind kind, Guid? companyId, Guid? applicationId, Guid? noteId)
    {
        Where(r => r.Kind == kind && r.CompanyId == companyId && r.ApplicationId == applicationId && r.NoteId == noteId);
        return this;
    }

    public IModelRunQuery InOrder()
    {
        Apply(q => q.OrderBy(r => r.DateCreated).ThenBy(r => r.Id));
        return this;
    }
}
