using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public sealed class CompanyRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Company, Guid>(pagingTokenHelper), ICompanyRepository;
