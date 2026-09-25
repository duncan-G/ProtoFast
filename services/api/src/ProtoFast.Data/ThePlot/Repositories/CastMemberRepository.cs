using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class CastMemberRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<CastMember, Guid>(pagingTokenHelper), ICastMemberRepository;
