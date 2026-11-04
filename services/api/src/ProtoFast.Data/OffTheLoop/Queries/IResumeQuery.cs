using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Queries;

public interface IResumeQuery : IQuery<Resume>
{
    IResumeQuery WithId(Guid id);

    /// <summary>The general resume when null.</summary>
    IResumeQuery ForApplication(Guid? applicationId);

    /// <summary>The general resume, then the tailored ones, newest first.</summary>
    IResumeQuery InOrder();
}
