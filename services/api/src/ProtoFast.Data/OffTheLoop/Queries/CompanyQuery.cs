using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop.Queries;

public sealed class CompanyQuery : Query<Company>, ICompanyQuery
{
    public ICompanyQuery WithId(Guid id)
    {
        Where(c => c.Id == id);
        return this;
    }

    public ICompanyQuery ByName()
    {
        Apply(q => q.OrderBy(c => c.Name.ToLower()).ThenBy(c => c.Id));
        return this;
    }
}
