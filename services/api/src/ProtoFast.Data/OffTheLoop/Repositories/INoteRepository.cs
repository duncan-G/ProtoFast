using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public interface INoteRepository : IRepository<Note, Guid>;
