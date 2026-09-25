using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class SceneElementQuery : Query<SceneElement>, ISceneElementQuery
{
    public ISceneElementQuery WithId(Guid id)
    {
        Where(e => e.Id == id);
        return this;
    }

    public ISceneElementQuery InScene(Guid sceneId)
    {
        Where(e => e.SceneId == sceneId);
        return this;
    }

    public ISceneElementQuery InStory(Guid storyId)
    {
        Where(e => e.Scene.Container.StoryId == storyId);
        return this;
    }

    public ISceneElementQuery OfType(SceneElementType type)
    {
        Where(e => e.Type == type);
        return this;
    }

    public ISceneElementQuery SpokenBy(Guid castMemberId)
    {
        Where(e => e.SpeakerId == castMemberId);
        return this;
    }

    public ISceneElementQuery AtLocation(Guid locationId)
    {
        Where(e => e.LocationId == locationId);
        return this;
    }

    public ISceneElementQuery InPositionRange(int from, int to = int.MaxValue)
    {
        Where(e => e.Position >= from && e.Position < to);
        return this;
    }

    public ISceneElementQuery MentioningCastMember(Guid castMemberId)
    {
        Where(e => e.Mentions.Any(m => m.CastMemberId == castMemberId));
        return this;
    }

    public ISceneElementQuery MentioningProp(Guid propId)
    {
        Where(e => e.Mentions.Any(m => m.PropId == propId));
        return this;
    }

    public ISceneElementQuery WithMentions()
    {
        Apply(q => q.Include(e => e.Mentions.OrderBy(m => m.Offset)));
        return this;
    }

    public ISceneElementQuery InOrder()
    {
        Apply(q => q.OrderBy(e => e.Position).ThenBy(e => e.Id));
        return this;
    }
}
