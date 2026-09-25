using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class TransitionRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Transition, Guid>(pagingTokenHelper), ITransitionRepository;
