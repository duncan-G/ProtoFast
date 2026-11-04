using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public sealed class ApplicationRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Application, Guid>(pagingTokenHelper), IApplicationRepository;
