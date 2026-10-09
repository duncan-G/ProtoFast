using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class ExtraQuery : Query<Extra>, IExtraQuery
{
    public IExtraQuery WithId(Guid id)
    {
        Where(e => e.Id == id);
        return this;
    }

    public IExtraQuery InStory(Guid storyId)
    {
        Where(e => e.StoryId == storyId);
        return this;
    }
}
