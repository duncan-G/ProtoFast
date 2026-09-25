using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class SceneElementMentionRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<SceneElementMention, Guid>(pagingTokenHelper), ISceneElementMentionRepository;
