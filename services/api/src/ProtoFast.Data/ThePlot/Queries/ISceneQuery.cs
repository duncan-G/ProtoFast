using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Scene"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ISceneRepository"/>.
/// </summary>
public interface ISceneQuery : IQuery<Scene>
{
    ISceneQuery WithId(Guid id);

    ISceneQuery InContainer(Guid containerId);

    ISceneQuery InStory(Guid storyId);

    ISceneQuery AtPosition(int position);

    ISceneQuery AtOrAfterPosition(int position);

    /// <summary>Includes the elements in order, with their mentions, speaker, location, time of day and transition.</summary>
    ISceneQuery WithElements();

    /// <summary>By container, then by position within the container.</summary>
    ISceneQuery InOrder();
}
