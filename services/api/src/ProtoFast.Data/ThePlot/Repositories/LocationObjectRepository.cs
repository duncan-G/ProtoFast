using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class LocationObjectRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<LocationObject, Guid>(pagingTokenHelper), ILocationObjectRepository;
