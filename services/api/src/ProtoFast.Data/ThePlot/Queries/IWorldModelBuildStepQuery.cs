using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="WorldModelBuildStep"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IWorldModelBuildStepRepository"/>.
/// </summary>
public interface IWorldModelBuildStepQuery : IQuery<WorldModelBuildStep>
{
    IWorldModelBuildStepQuery WithId(Guid id);

    IWorldModelBuildStepQuery InBuild(Guid buildId);

    IWorldModelBuildStepQuery OfKind(WorldModelStepKind kind);

    IWorldModelBuildStepQuery WithStatus(WorldModelStepStatus status);

    IWorldModelBuildStepQuery InOrder();
}
