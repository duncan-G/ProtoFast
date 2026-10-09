using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="MediaAsset"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IMediaAssetRepository"/>.
/// </summary>
public interface IMediaAssetQuery : IQuery<MediaAsset>
{
    IMediaAssetQuery WithId(Guid id);

    IMediaAssetQuery InStory(Guid storyId);

    IMediaAssetQuery OfKind(MediaAssetKind kind);

    IMediaAssetQuery WithRequestHash(string requestHash);
}
