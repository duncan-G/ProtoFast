using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="LocationPart"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ILocationPartRepository"/>.
/// </summary>
public interface ILocationPartQuery : IQuery<LocationPart>
{
    ILocationPartQuery WithId(Guid id);

    ILocationPartQuery InLocation(Guid locationId);

    ILocationPartQuery InStory(Guid storyId);

    ILocationPartQuery OfOrigin(NodeOrigin origin);

    /// <summary>Parts with no observation placing anything in them, so a rebuild can drop what it no longer reads.</summary>
    ILocationPartQuery Unobserved();

    /// <summary>By position, then name.</summary>
    ILocationPartQuery InOrder();
}
