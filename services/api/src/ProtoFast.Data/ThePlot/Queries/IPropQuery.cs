using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Prop"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.IPropRepository"/>.
/// </summary>
public interface IPropQuery : IQuery<Prop>
{
    IPropQuery WithId(Guid id);

    IPropQuery InStory(Guid storyId);

    /// <inheritdoc cref="ICastMemberQuery.WithName"/>
    IPropQuery WithName(string name);

    /// <summary>Case-insensitive substring match, for <c>@</c> autocomplete.</summary>
    IPropQuery NameContains(string text);

    IPropQuery ByName();
}
