using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class LocationRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Location, Guid>(pagingTokenHelper), ILocationRepository;
