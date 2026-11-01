using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class LocationObservationRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<LocationObservation, Guid>(pagingTokenHelper), ILocationObservationRepository;
