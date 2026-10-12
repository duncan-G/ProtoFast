using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class LocationObservationQuery : Query<LocationObservation>, ILocationObservationQuery
{
    public ILocationObservationQuery InStory(Guid storyId)
    {
        Where(o => o.SceneElement.Scene.Container.StoryId == storyId);
        return this;
    }

    public ILocationObservationQuery InScene(Guid sceneId)
    {
        Where(o => o.SceneElement.SceneId == sceneId);
        return this;
    }

    public ILocationObservationQuery OfOrigin(ObservationOrigin origin)
    {
        Where(o => o.Origin == origin);
        return this;
    }

    public ILocationObservationQuery NotAuthored()
    {
        Where(o => o.Origin != ObservationOrigin.Author);
        return this;
    }

    public ILocationObservationQuery InStoryOrder()
    {
        Apply(q => q
            .OrderBy(o => o.SceneElement.Scene.Container.Position)
            .ThenBy(o => o.SceneElement.Scene.Position)
            .ThenBy(o => o.SceneElement.Position)
            .ThenBy(o => o.Id));
        return this;
    }
}
