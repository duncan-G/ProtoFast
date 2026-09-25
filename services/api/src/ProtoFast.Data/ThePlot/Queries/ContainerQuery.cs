using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class ContainerQuery : Query<Container>, IContainerQuery
{
    public IContainerQuery WithId(Guid id)
    {
        Where(c => c.Id == id);
        return this;
    }

    public IContainerQuery InStory(Guid storyId)
    {
        Where(c => c.StoryId == storyId);
        return this;
    }

    public IContainerQuery AtOrAfterPosition(int position)
    {
        Where(c => c.Position >= position);
        return this;
    }

    public IContainerQuery InOrder()
    {
        Apply(q => q.OrderBy(c => c.Position).ThenBy(c => c.Id));
        return this;
    }
}
