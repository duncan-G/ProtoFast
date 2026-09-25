using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class SceneQuery : Query<Scene>, ISceneQuery
{
    public ISceneQuery WithId(Guid id)
    {
        Where(s => s.Id == id);
        return this;
    }

    public ISceneQuery InAct(Guid actId)
    {
        Where(s => s.ActId == actId);
        return this;
    }

    public ISceneQuery InDraft(Guid draftId)
    {
        Where(s => s.Act.DraftId == draftId);
        return this;
    }

    public ISceneQuery AtPosition(int position)
    {
        Where(s => s.Position == position);
        return this;
    }

    public ISceneQuery AtOrAfterPosition(int position)
    {
        Where(s => s.Position >= position);
        return this;
    }

    public ISceneQuery WithElements()
    {
        Apply(q => q
            .Include(s => s.Elements.OrderBy(e => e.Position).ThenBy(e => e.Id))
            .ThenInclude(e => e.Mentions.OrderBy(m => m.Offset))
            .Include(s => s.Elements).ThenInclude(e => e.Speaker)
            .Include(s => s.Elements).ThenInclude(e => e.Location)
            .AsSplitQuery());
        return this;
    }

    public ISceneQuery InOrder()
    {
        Apply(q => q.OrderBy(s => s.Act.Position).ThenBy(s => s.Position).ThenBy(s => s.Id));
        return this;
    }
}
