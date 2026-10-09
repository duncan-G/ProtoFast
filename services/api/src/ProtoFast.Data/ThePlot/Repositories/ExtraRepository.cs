using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class ExtraRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Extra, Guid>(pagingTokenHelper), IExtraRepository;
