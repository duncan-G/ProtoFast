using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="SceneElementMention"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ISceneElementMentionRepository"/>.
/// </summary>
public interface ISceneElementMentionQuery : IQuery<SceneElementMention>
{
    /// <summary>A beat's mentions: the set to replace when its text is saved.</summary>
    ISceneElementMentionQuery InElement(Guid sceneElementId);

    ISceneElementMentionQuery InScene(Guid sceneId);

    ISceneElementMentionQuery InDraft(Guid draftId);

    /// <summary>Where a cast member is mentioned: the editor's "N mentions" count, and a rename's rewrite set.</summary>
    ISceneElementMentionQuery OfCastMember(Guid castMemberId);

    /// <summary>Where a prop is referenced: "Props in play", and a rename's rewrite set.</summary>
    ISceneElementMentionQuery OfProp(Guid propId);
}
