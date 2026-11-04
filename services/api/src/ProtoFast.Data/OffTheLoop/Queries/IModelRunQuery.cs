using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Queries;

public interface IModelRunQuery : IQuery<ModelRun>
{
    IModelRunQuery WithId(Guid id);

    IModelRunQuery WithIds(IReadOnlyCollection<Guid> ids);

    IModelRunQuery WithStatus(params ModelRunStatus[] statuses);

    /// <summary>The same work on the same thing: a null id matches only a null one.</summary>
    IModelRunQuery OnSubject(ModelRunKind kind, Guid? companyId, Guid? applicationId, Guid? noteId);

    /// <summary>Oldest first.</summary>
    IModelRunQuery InOrder();
}
