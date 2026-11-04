using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public interface IDocumentRepository : IRepository<Document, Guid>;
