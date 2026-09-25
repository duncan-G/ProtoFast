using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class CharacterRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<Character, Guid>(pagingTokenHelper), ICharacterRepository;
