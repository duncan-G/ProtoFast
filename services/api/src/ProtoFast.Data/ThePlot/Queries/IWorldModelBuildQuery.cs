using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="WorldModelBuild"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IWorldModelBuildRepository"/>.
/// </summary>
public interface IWorldModelBuildQuery : IQuery<WorldModelBuild>
{
    IWorldModelBuildQuery WithId(Guid id);

    IWorldModelBuildQuery InStory(Guid storyId);

    /// <summary>Queued, running or waiting to be retried.</summary>
    IWorldModelBuildQuery Active();

    IWorldModelBuildQuery WithSteps();

    IWorldModelBuildQuery NewestFirst();
}
