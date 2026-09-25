using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Repositories;

public sealed class CharacterKindRepository(PagingTokenHelper pagingTokenHelper)
    : Repository<CharacterKind, Guid>(pagingTokenHelper), ICharacterKindRepository;
