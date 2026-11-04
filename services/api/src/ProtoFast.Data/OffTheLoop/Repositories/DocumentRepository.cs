using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public sealed class DocumentRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Document, Guid>(pagingTokenHelper), IDocumentRepository;
