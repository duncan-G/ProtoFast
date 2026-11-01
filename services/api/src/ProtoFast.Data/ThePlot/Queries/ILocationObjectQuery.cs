using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="LocationObject"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ILocationObjectRepository"/>.
/// </summary>
public interface ILocationObjectQuery : IQuery<LocationObject>
{
    ILocationObjectQuery WithId(Guid id);

    ILocationObjectQuery InStory(Guid storyId);

    ILocationObjectQuery OfKind(ObjectKind kind);

    /// <summary>Objects no observation mentions, as a place, a way through or a subject.</summary>
    ILocationObjectQuery Unobserved();

    ILocationObjectQuery ByName();
}
