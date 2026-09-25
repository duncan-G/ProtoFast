using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class LocationQuery : Query<Location>, ILocationQuery
{
    public ILocationQuery WithId(Guid id)
    {
        Where(l => l.Id == id);
        return this;
    }

    public ILocationQuery InStory(Guid storyId)
    {
        Where(l => l.StoryId == storyId);
        return this;
    }

    public ILocationQuery WithName(string name)
    {
        string lowered = name.ToLower();
        Where(l => l.Name.ToLower() == lowered);
        return this;
    }

    public ILocationQuery NameContains(string text)
    {
        string lowered = text.ToLower();
        Where(l => l.Name.ToLower().Contains(lowered));
        return this;
    }

    public ILocationQuery WithSetting(LocationSetting setting)
    {
        Where(l => l.Setting == setting);
        return this;
    }

    public ILocationQuery ByName()
    {
        Apply(q => q.OrderBy(l => l.Name).ThenBy(l => l.Id));
        return this;
    }
}
