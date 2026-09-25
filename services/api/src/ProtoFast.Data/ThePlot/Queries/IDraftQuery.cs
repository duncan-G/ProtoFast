using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Draft"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IDraftRepository"/>.
/// </summary>
public interface IDraftQuery : IQuery<Draft>
{
    IDraftQuery WithId(Guid id);

    IDraftQuery InStory(Guid storyId);

    IDraftQuery WithNumber(int number);

    /// <summary>
    /// Loads the draft's outline: its acts and each act's scenes, in order, without the scenes'
    /// elements. Enough for the breadcrumb, scene navigation and the "next scene" preview's title.
    /// </summary>
    IDraftQuery WithOutline();

    /// <summary>Highest draft number first, so the first row is the latest draft.</summary>
    IDraftQuery LatestFirst();
}
