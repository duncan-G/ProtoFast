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

    ISceneQuery InAct(Guid actId);

    ISceneQuery InDraft(Guid draftId);

    ISceneQuery AtPosition(int position);

    /// <summary>Scenes at or after a position: the ones to shift when inserting or removing one.</summary>
    ISceneQuery AtOrAfterPosition(int position);

    /// <summary>
    /// Loads everything the scene editor renders: the elements in order, each with its mentions,
    /// speaker and location. The library itself comes from <see cref="IStoryQuery.WithLibrary"/>.
    /// </summary>
    ISceneQuery WithElements();

    /// <summary>Draft order — by act, then position within the act — ties broken by id.</summary>
    ISceneQuery InOrder();
}
