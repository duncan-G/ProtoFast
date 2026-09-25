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

    /// <summary>Dialogue beats the cast member speaks: the editor's "N lines" count.</summary>
    ISceneElementQuery SpokenBy(Guid castMemberId);

    /// <summary>Headings set at the location: the library's "in scene ×N" count.</summary>
    ISceneElementQuery AtLocation(Guid locationId);

    /// <summary>
    /// Elements in the range <c>[from, to)</c>, for moving a heading together with the beats it
    /// opens, or shifting the tail of the flow when inserting.
    /// </summary>
    ISceneElementQuery InPositionRange(int from, int to = int.MaxValue);

    ISceneElementQuery WithMentions();

    /// <summary>Scene order, ties broken by id.</summary>
    ISceneElementQuery InOrder();
}
