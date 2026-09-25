using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class ActRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Act, Guid>(pagingTokenHelper), IActRepository;
