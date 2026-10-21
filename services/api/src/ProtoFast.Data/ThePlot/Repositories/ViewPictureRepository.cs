using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class ViewPictureRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<ViewPicture, Guid>(pagingTokenHelper), IViewPictureRepository;
