using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class DocumentRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Document, string>(pagingTokenHelper), IDocumentRepository;
