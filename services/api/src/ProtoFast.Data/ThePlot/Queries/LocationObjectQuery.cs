using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class LocationObjectQuery : Query<LocationObject>, ILocationObjectQuery
{
    public ILocationObjectQuery WithId(Guid id)
    {
        Where(o => o.Id == id);
        return this;
    }

    public ILocationObjectQuery InStory(Guid storyId)
    {
        Where(o => o.StoryId == storyId);
        return this;
    }

    public ILocationObjectQuery OfKind(ObjectKind kind)
    {
        Where(o => o.Kind == kind);
        return this;
    }

    public ILocationObjectQuery Unobserved()
    {
        Where(o => !o.Story.Containers
            .SelectMany(c => c.Scenes)
            .SelectMany(s => s.Elements)
            .SelectMany(e => e.Observations)
            .Any(x => x.ObjectId == o.Id || x.ViaObjectId == o.Id));
        return this;
    }

    public ILocationObjectQuery ByName()
    {
        Apply(q => q.OrderBy(o => o.Name).ThenBy(o => o.Id));
        return this;
    }
}
