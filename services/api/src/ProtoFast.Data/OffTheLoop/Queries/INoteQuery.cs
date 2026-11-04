using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Queries;

public interface INoteQuery : IQuery<Note>
{
    INoteQuery WithId(Guid id);

    INoteQuery OfKind(NoteKind kind);

    INoteQuery RecentlyModifiedFirst();
}
