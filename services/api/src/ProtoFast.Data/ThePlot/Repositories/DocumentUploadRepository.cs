using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class DocumentUploadRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<DocumentUpload, string>(pagingTokenHelper), IDocumentUploadRepository;
