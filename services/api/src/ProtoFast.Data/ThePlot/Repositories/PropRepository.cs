using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class PropRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Prop, Guid>(pagingTokenHelper), IPropRepository;
