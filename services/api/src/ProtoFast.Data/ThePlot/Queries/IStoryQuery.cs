using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Story"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IStoryRepository"/>.
/// </summary>
public interface IStoryQuery : IQuery<Story>
{
    IStoryQuery WithId(Guid id);

    /// <summary>Case-insensitive.</summary>
    IStoryQuery TitleContains(string text);

    /// <summary>Includes the characters, locations and props, each ordered by name.</summary>
    IStoryQuery WithLibrary();

    /// <summary>Includes the containers and their scenes in order, without the scenes' elements.</summary>
    IStoryQuery WithOutline();

    IStoryQuery RecentlyModifiedFirst();
}
