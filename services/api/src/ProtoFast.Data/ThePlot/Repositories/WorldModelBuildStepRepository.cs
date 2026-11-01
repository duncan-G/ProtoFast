using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class WorldModelBuildStepRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<WorldModelBuildStep, Guid>(pagingTokenHelper), IWorldModelBuildStepRepository;
