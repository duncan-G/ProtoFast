using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class DraftRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Draft, Guid>(pagingTokenHelper), IDraftRepository;
