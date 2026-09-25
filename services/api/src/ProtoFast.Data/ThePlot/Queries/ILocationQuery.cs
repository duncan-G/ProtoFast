using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Location"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ILocationRepository"/>.
/// </summary>
public interface ILocationQuery : IQuery<Location>
{
    ILocationQuery WithId(Guid id);

    ILocationQuery InStory(Guid storyId);

    /// <summary>Case-insensitive, as the editor matches names.</summary>
    ILocationQuery WithName(string name);

    /// <summary>Case-insensitive.</summary>
    ILocationQuery NameContains(string text);

    ILocationQuery WithSetting(LocationSetting setting);

    ILocationQuery ByName();
}
