using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class MediaAssetQuery : Query<MediaAsset>, IMediaAssetQuery
{
    public IMediaAssetQuery WithId(Guid id)
    {
        Where(a => a.Id == id);
        return this;
    }

    public IMediaAssetQuery InStory(Guid storyId)
    {
        Where(a => a.StoryId == storyId);
        return this;
    }

    public IMediaAssetQuery OfKind(MediaAssetKind kind)
    {
        Where(a => a.Kind == kind);
        return this;
    }

    public IMediaAssetQuery WithRequestHash(string requestHash)
    {
        Where(a => a.RequestHash == requestHash);
        return this;
    }
}
