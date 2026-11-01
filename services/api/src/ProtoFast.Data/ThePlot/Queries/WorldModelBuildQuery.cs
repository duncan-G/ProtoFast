using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class WorldModelBuildQuery : Query<WorldModelBuild>, IWorldModelBuildQuery
{
    public IWorldModelBuildQuery WithId(Guid id)
    {
        Where(b => b.Id == id);
        return this;
    }

    public IWorldModelBuildQuery InStory(Guid storyId)
    {
        Where(b => b.StoryId == storyId);
        return this;
    }

    public IWorldModelBuildQuery Active()
    {
        Where(b => b.Status == WorldModelBuildStatus.Queued
                   || b.Status == WorldModelBuildStatus.Running
                   || b.Status == WorldModelBuildStatus.Retrying);
        return this;
    }

    public IWorldModelBuildQuery WithSteps()
    {
        Apply(q => q.Include(b => b.Steps.OrderBy(s => s.Sequence)));
        return this;
    }

    public IWorldModelBuildQuery NewestFirst()
    {
        Apply(q => q.OrderByDescending(b => b.DateCreated).ThenByDescending(b => b.Id));
        return this;
    }
}
