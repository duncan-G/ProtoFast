using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public sealed class ModelRunRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<ModelRun, Guid>(pagingTokenHelper), IModelRunRepository;
