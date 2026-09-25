using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class ContainerRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Container, Guid>(pagingTokenHelper), IContainerRepository;
