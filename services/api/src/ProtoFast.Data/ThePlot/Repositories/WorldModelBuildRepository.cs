using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class WorldModelBuildRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<WorldModelBuild, Guid>(pagingTokenHelper), IWorldModelBuildRepository;
