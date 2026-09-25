using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class PropQuery : Query<Prop>, IPropQuery
{
    public IPropQuery WithId(Guid id)
    {
        Where(p => p.Id == id);
        return this;
    }

    public IPropQuery InStory(Guid storyId)
    {
        Where(p => p.StoryId == storyId);
        return this;
    }

    public IPropQuery WithName(string name)
    {
        string lowered = name.ToLower();
        Where(p => p.Name.ToLower() == lowered);
        return this;
    }

    public IPropQuery NameContains(string text)
    {
        string lowered = text.ToLower();
        Where(p => p.Name.ToLower().Contains(lowered));
        return this;
    }

    public IPropQuery ByName()
    {
        Apply(q => q.OrderBy(p => p.Name).ThenBy(p => p.Id));
        return this;
    }
}
