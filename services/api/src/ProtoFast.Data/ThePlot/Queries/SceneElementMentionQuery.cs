using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class SceneElementMentionQuery : Query<SceneElementMention>, ISceneElementMentionQuery
{
    public ISceneElementMentionQuery InElement(Guid sceneElementId)
    {
        Where(m => m.SceneElementId == sceneElementId);
        return this;
    }

    public ISceneElementMentionQuery InScene(Guid sceneId)
    {
        Where(m => m.SceneElement.SceneId == sceneId);
        return this;
    }

    public ISceneElementMentionQuery InDraft(Guid draftId)
    {
        Where(m => m.SceneElement.Scene.Act.DraftId == draftId);
        return this;
    }

    public ISceneElementMentionQuery OfCastMember(Guid castMemberId)
    {
        Where(m => m.CastMemberId == castMemberId);
        return this;
    }

    public ISceneElementMentionQuery OfProp(Guid propId)
    {
        Where(m => m.PropId == propId);
        return this;
    }
}
