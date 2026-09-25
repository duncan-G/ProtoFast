using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class StoryQuery : Query<Story>, IStoryQuery
{
    public IStoryQuery WithId(Guid id)
    {
        Where(s => s.Id == id);
        return this;
    }

    public IStoryQuery FromDocument(string documentId)
    {
        Where(s => s.SourceDocumentId == documentId);
        return this;
    }

    public IStoryQuery TitleContains(string text)
    {
        string lowered = text.ToLower();
        Where(s => s.Title.ToLower().Contains(lowered));
        return this;
    }

    public IStoryQuery WithLibrary()
    {
        Apply(q => q
            .Include(s => s.Cast.OrderBy(c => c.Name))
            .Include(s => s.Locations.OrderBy(l => l.Name))
            .Include(s => s.Props.OrderBy(p => p.Name))
            .AsSplitQuery());
        return this;
    }

    public IStoryQuery WithOutline()
    {
        Apply(q => q
            .Include(s => s.Containers.OrderBy(c => c.Position).ThenBy(c => c.Id))
            .ThenInclude(c => c.Scenes.OrderBy(sc => sc.Position).ThenBy(sc => sc.Id))
            .AsSplitQuery());
        return this;
    }

    public IStoryQuery RecentlyModifiedFirst()
    {
        Apply(q => q.OrderByDescending(s => s.DateLastModified).ThenByDescending(s => s.Id));
        return this;
    }
}
