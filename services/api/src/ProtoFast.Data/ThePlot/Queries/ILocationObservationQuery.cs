using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="LocationObservation"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ILocationObservationRepository"/>.
/// </summary>
public interface ILocationObservationQuery : IQuery<LocationObservation>
{
    ILocationObservationQuery InStory(Guid storyId);

    ILocationObservationQuery InScene(Guid sceneId);

    ILocationObservationQuery OfOrigin(ObservationOrigin origin);

    ILocationObservationQuery NotAuthored();

    /// <summary>Story time: container, scene, then beat position.</summary>
    ILocationObservationQuery InStoryOrder();
}
