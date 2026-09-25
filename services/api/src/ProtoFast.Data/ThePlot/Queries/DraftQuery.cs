using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class DraftQuery : Query<Draft>, IDraftQuery
{
    public IDraftQuery WithId(Guid id)
    {
        Where(d => d.Id == id);
        return this;
    }

    public IDraftQuery InStory(Guid storyId)
    {
        Where(d => d.StoryId == storyId);
        return this;
    }

    public IDraftQuery WithNumber(int number)
    {
        Where(d => d.Number == number);
        return this;
    }

    public IDraftQuery WithOutline()
    {
        Apply(q => q
            .Include(d => d.Acts.OrderBy(a => a.Position).ThenBy(a => a.Id))
            .ThenInclude(a => a.Scenes.OrderBy(s => s.Position).ThenBy(s => s.Id))
            .AsSplitQuery());
        return this;
    }

    public IDraftQuery LatestFirst()
    {
        Apply(q => q.OrderByDescending(d => d.Number));
        return this;
    }
}
