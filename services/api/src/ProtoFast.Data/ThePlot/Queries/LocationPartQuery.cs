using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class LocationPartQuery : Query<LocationPart>, ILocationPartQuery
{
    public ILocationPartQuery WithId(Guid id)
    {
        Where(p => p.Id == id);
        return this;
    }

    public ILocationPartQuery InLocation(Guid locationId)
    {
        Where(p => p.LocationId == locationId);
        return this;
    }

    public ILocationPartQuery InStory(Guid storyId)
    {
        Where(p => p.Location.StoryId == storyId);
        return this;
    }

    public ILocationPartQuery OfOrigin(NodeOrigin origin)
    {
        Where(p => p.Origin == origin);
        return this;
    }

    public ILocationPartQuery Unobserved()
    {
        Where(p => !p.Location.Story.Containers
            .SelectMany(c => c.Scenes)
            .SelectMany(s => s.Elements)
            .SelectMany(e => e.Observations)
            .Any(o => o.PlacePartId == p.Id || o.OtherPartId == p.Id));
        return this;
    }

    public ILocationPartQuery InOrder()
    {
        Apply(q => q.OrderBy(p => p.Position).ThenBy(p => p.Name).ThenBy(p => p.Id));
        return this;
    }
}
