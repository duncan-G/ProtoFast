using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public sealed class TodoRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Todo, Guid>(pagingTokenHelper), ITodoRepository;
