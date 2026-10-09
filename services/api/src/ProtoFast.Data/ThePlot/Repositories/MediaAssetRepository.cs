using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class MediaAssetRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<MediaAsset, Guid>(pagingTokenHelper), IMediaAssetRepository;
