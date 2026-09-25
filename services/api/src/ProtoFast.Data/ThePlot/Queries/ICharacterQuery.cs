using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Queries;

/// <summary>
/// Composable filters over <see cref="Character"/>. Obtain one from
/// <see cref="IQueryFactory{TEntity,TQuery}"/> and hand it to <see cref="Repositories.ICharacterRepository"/>.
/// </summary>
public interface ICharacterQuery : IQuery<Character>
{
    ICharacterQuery WithId(Guid id);

    ICharacterQuery InStory(Guid storyId);

    /// <summary>Case-insensitive, as the editor matches names.</summary>
    ICharacterQuery WithName(string name);

    /// <summary>Case-insensitive.</summary>
    ICharacterQuery NameContains(string text);

    ICharacterQuery OfKind(CharacterKind kind);

    ICharacterQuery ByName();
}
