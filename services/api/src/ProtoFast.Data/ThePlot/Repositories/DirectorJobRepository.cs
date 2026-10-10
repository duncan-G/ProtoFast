using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class DirectorJobRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<DirectorJob, Guid>(pagingTokenHelper), IDirectorJobRepository;
