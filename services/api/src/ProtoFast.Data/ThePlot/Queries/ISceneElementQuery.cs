using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="SceneElement"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ISceneElementRepository"/>.
/// </summary>
public interface ISceneElementQuery : IQuery<SceneElement>
{
    ISceneElementQuery WithId(Guid id);

    ISceneElementQuery InScene(Guid sceneId);

    ISceneElementQuery InStory(Guid storyId);

    ISceneElementQuery OfType(SceneElementType type);

    ISceneElementQuery SpokenBy(Guid castMemberId);

    ISceneElementQuery AtLocation(Guid locationId);

    /// <summary>Positions in <c>[from, to)</c>.</summary>
    ISceneElementQuery InPositionRange(int from, int to = int.MaxValue);

    ISceneElementQuery MentioningCastMember(Guid castMemberId);

    ISceneElementQuery MentioningProp(Guid propId);

    ISceneElementQuery WithMentions();

    ISceneElementQuery InOrder();
}
