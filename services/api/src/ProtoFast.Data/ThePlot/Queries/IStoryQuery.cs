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

    /// <summary>Stories adapted from the given imported <see cref="Document"/>.</summary>
    IStoryQuery FromDocument(string documentId);

    /// <summary>Case-insensitive substring match on the title, for the desk's search box.</summary>
    IStoryQuery TitleContains(string text);

    /// <summary>Loads the story library: cast, locations and props, each by name.</summary>
    IStoryQuery WithLibrary();

    /// <summary>Most recently modified first, ties broken by id.</summary>
    IStoryQuery RecentlyModifiedFirst();
}
