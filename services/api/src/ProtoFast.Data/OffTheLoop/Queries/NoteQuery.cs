using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop.Queries;

public sealed class NoteQuery : Query<Note>, INoteQuery
{
    public INoteQuery WithId(Guid id)
    {
        Where(n => n.Id == id);
        return this;
    }

    public INoteQuery RecentlyModifiedFirst()
    {
        Apply(q => q.OrderByDescending(n => n.DateLastModified).ThenByDescending(n => n.Id));
        return this;
    }
}
