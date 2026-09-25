using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class ActQuery : Query<Act>, IActQuery
{
    public IActQuery WithId(Guid id)
    {
        Where(a => a.Id == id);
        return this;
    }

    public IActQuery InStory(Guid storyId)
    {
        Where(a => a.StoryId == storyId);
        return this;
    }

    public IActQuery AtOrAfterPosition(int position)
    {
        Where(a => a.Position >= position);
        return this;
    }

    public IActQuery InOrder()
    {
        Apply(q => q.OrderBy(a => a.Position).ThenBy(a => a.Id));
        return this;
    }
}
