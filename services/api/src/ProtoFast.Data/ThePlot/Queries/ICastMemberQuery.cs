using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="CastMember"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ICastMemberRepository"/>.
/// </summary>
public interface ICastMemberQuery : IQuery<CastMember>
{
    ICastMemberQuery WithId(Guid id);

    ICastMemberQuery InStory(Guid storyId);

    /// <summary>Case-insensitive exact name match: the clash check before adding or renaming.</summary>
    ICastMemberQuery WithName(string name);

    /// <summary>Case-insensitive substring match, for the speaker picker and <c>@</c> autocomplete.</summary>
    ICastMemberQuery NameContains(string text);

    ICastMemberQuery OfKind(CastMemberKind kind);

    ICastMemberQuery ByName();
}
