using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public sealed class GoalRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Goal, Guid>(pagingTokenHelper), IGoalRepository;
