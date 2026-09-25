using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class TransitionQuery : Query<Transition>, ITransitionQuery
{
    public ITransitionQuery WithId(Guid id)
    {
        Where(v => v.Id == id);
        return this;
    }

    public ITransitionQuery InStory(Guid storyId)
    {
        Where(v => v.StoryId == storyId);
        return this;
    }

    public ITransitionQuery WithLabel(string label)
    {
        string lowered = label.ToLower();
        Where(v => v.Label.ToLower() == lowered);
        return this;
    }

    public ITransitionQuery InOrder()
    {
        Apply(q => q.OrderBy(v => v.Position).ThenBy(v => v.Id));
        return this;
    }
}
