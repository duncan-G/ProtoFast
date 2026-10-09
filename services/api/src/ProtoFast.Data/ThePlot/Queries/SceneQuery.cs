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

    public ISceneQuery InContainer(Guid containerId)
    {
        Where(s => s.ContainerId == containerId);
        return this;
    }

    public ISceneQuery InStory(Guid storyId)
    {
        Where(s => s.Container.StoryId == storyId);
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

    public ISceneQuery WithDirection()
    {
        Apply(q => q
            .Include(s => s.Frames.OrderBy(p => p.Position).ThenBy(p => p.Id))
            .ThenInclude(p => p.Placements)
            .Include(s => s.Frames).ThenInclude(p => p.Props)
            .Include(s => s.Frames).ThenInclude(p => p.Extras)
            .Include(s => s.Frames).ThenInclude(p => p.Image)
            .Include(s => s.Cues).ThenInclude(c => c.Audio)
            .Include(s => s.Appearances).ThenInclude(a => a.Image)
            .Include(s => s.LookChanges).ThenInclude(c => c.Image)
            .Include(s => s.PropStates)
            .Include(s => s.LocationStates)
            .Include(s => s.Extras)
            .AsSplitQuery());
        return this;
    }

    public ISceneQuery InOrder()
    {
        Apply(q => q.OrderBy(s => s.Container.Position).ThenBy(s => s.Position).ThenBy(s => s.Id));
        return this;
    }
}
