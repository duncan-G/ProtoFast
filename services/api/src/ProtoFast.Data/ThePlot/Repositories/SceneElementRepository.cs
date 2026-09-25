using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class SceneElementRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<SceneElement, Guid>(pagingTokenHelper), ISceneElementRepository;
