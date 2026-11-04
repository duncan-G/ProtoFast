using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Queries;

public interface ICompanyQuery : IQuery<Company>
{
    ICompanyQuery WithId(Guid id);

    /// <summary>Ignoring case.</summary>
    ICompanyQuery ByName();
}
