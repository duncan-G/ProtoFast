using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Repositories;

public sealed class ResumeRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Resume, Guid>(pagingTokenHelper), IResumeRepository;
