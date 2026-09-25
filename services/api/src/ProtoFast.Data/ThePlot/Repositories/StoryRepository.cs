using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class StoryRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Story, Guid>(pagingTokenHelper), IStoryRepository;
