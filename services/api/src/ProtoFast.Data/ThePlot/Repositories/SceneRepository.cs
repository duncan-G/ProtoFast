using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class SceneRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Scene, Guid>(pagingTokenHelper), ISceneRepository;
