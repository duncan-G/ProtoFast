using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class TimeOfDayQuery : Query<TimeOfDay>, ITimeOfDayQuery
{
    public ITimeOfDayQuery WithId(Guid id)
    {
        Where(v => v.Id == id);
        return this;
    }

    public ITimeOfDayQuery InStory(Guid storyId)
    {
        Where(v => v.StoryId == storyId);
        return this;
    }

    public ITimeOfDayQuery WithLabel(string label)
    {
        string lowered = label.ToLower();
        Where(v => v.Label.ToLower() == lowered);
        return this;
    }

    public ITimeOfDayQuery InOrder()
    {
        Apply(q => q.OrderBy(v => v.Position).ThenBy(v => v.Id));
        return this;
    }
}
