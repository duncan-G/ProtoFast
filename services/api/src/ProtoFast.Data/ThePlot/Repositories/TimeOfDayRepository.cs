using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class TimeOfDayRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<TimeOfDay, Guid>(pagingTokenHelper), ITimeOfDayRepository;
