using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="CharacterKind"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ICharacterKindRepository"/>.
/// </summary>
public interface ICharacterKindQuery : IQuery<CharacterKind>
{
    ICharacterKindQuery WithId(Guid id);

    ICharacterKindQuery InStory(Guid storyId);

    /// <summary>Case-insensitive.</summary>
    ICharacterKindQuery WithLabel(string label);

    ICharacterKindQuery InOrder();
}
