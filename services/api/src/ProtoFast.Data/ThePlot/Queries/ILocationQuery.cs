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

    /// <inheritdoc cref="ICastMemberQuery.WithName"/>
    ILocationQuery WithName(string name);

    /// <summary>Case-insensitive substring match, for the heading's location picker.</summary>
    ILocationQuery NameContains(string text);

    /// <summary>Narrows the picker when the search starts with <c>INT.</c> or <c>EXT.</c>.</summary>
    ILocationQuery WithSetting(LocationSetting setting);

    ILocationQuery ByName();
}
